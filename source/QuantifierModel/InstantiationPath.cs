﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using Z3AxiomProfiler.PrettyPrinting;

namespace Z3AxiomProfiler.QuantifierModel
{
    public class InstantiationPath : IPrintable
    {
        private readonly List<Instantiation> pathInstantiations;

        public InstantiationPath()
        {
            pathInstantiations = new List<Instantiation>();
        }

        public InstantiationPath(InstantiationPath other) : this()
        {
            pathInstantiations.AddRange(other.pathInstantiations);
        }

        public InstantiationPath(Instantiation inst) : this()
        {
            pathInstantiations.Add(inst);
        }

        public double Cost()
        {
            return pathInstantiations.Sum(instantiation => instantiation.Cost);
        }

        public int Length()
        {
            return pathInstantiations.Count;
        }

        public void prepend(Instantiation inst)
        {
            pathInstantiations.Insert(0, inst);
        }

        public void append(Instantiation inst)
        {
            pathInstantiations.Add(inst);
        }

        public void appendWithOverlap(InstantiationPath other)
        {
            var joinIdx = other.pathInstantiations.FindIndex(inst => !pathInstantiations.Contains(inst));
            if (other.Length() == 0 || joinIdx == -1)
            {
                return;
            }
            pathInstantiations.AddRange(other.pathInstantiations.GetRange(joinIdx, other.pathInstantiations.Count - joinIdx));
        }

        public void InfoPanelText(InfoPanelContent content, PrettyPrintFormat format)
        {
            content.switchFormat(InfoPanelContent.TitleFont, Color.Black);
            content.Append("Path explanation:");
            content.switchToDefaultFormat();
            content.Append("\n\nLength: " + Length()).Append('\n');
            content.Append("Highlighted terms are ");
            content.switchFormat(InfoPanelContent.DefaultFont, Color.LimeGreen);
            content.Append("matched");
            content.switchToDefaultFormat();
            content.Append(" or ");
            content.switchFormat(InfoPanelContent.DefaultFont, Color.Coral);
            content.Append("blamed");
            content.switchToDefaultFormat();
            content.Append(" or ");
            content.switchFormat(InfoPanelContent.DefaultFont, Color.Goldenrod);
            content.Append("blamed using equality");
            content.switchToDefaultFormat();
            content.Append(" or ");
            content.switchFormat(InfoPanelContent.DefaultFont, Color.DeepSkyBlue);
            content.Append("bound");
            content.switchToDefaultFormat();
            content.Append(".\n\n");

            var pathEnumerator = pathInstantiations.GetEnumerator();
            if (!pathEnumerator.MoveNext() || pathEnumerator.Current == null) return; // empty path
            var current = pathEnumerator.Current;

            // first thing
            content.switchToDefaultFormat();
            content.Append("\nStarting from the following term(s):\n\n");
            current.tempHighlightBlameBindTerms(format);
            foreach (var distinctBlameTerm in current.bindingInfo.getDistinctBlameTerms())
            {
                distinctBlameTerm.PrettyPrint(content, format);
            }

            content.switchToDefaultFormat();
            content.Append("\n\nApplication of ");
            content.Append(current.Quant.PrintName);
            content.Append("\n\n");

            current.Quant.BodyTerm.PrettyPrint(content, format);
            format.restoreAllOriginalRules();

            while (pathEnumerator.MoveNext() && pathEnumerator.Current != null)
            {
                // between stuff
                var previous = current;
                current = pathEnumerator.Current;

                current.tempHighlightBlameBindTerms(format);

                content.switchToDefaultFormat();
                content.Append("\n\nThis instantiation yields:\n\n");
                previous.dependentTerms.Last().PrettyPrint(content, format);

                // Other prerequisites:
                var otherRequiredTerms = current.bindingInfo.getDistinctBlameTerms()
                    .FindAll(term => !previous.dependentTerms.Last().isSubterm(term)).ToList();
                if (otherRequiredTerms.Count > 0)
                {
                    content.switchToDefaultFormat();
                    content.Append("\n\nTogether with the following term(s):\n\n");
                    foreach (var distinctBlameTerm in otherRequiredTerms)
                    {
                        distinctBlameTerm.PrettyPrint(content, format);
                        content.Append('\n');
                    }
                }

                content.switchToDefaultFormat();
                content.Append("\n\nApplication of ");
                content.Append(current.Quant.PrintName);
                content.Append("\n\n");

                current.Quant.BodyTerm.PrettyPrint(content, format);
                format.restoreAllOriginalRules();
            }

            // Quantifier info for last in chain
            content.switchToDefaultFormat();
            content.Append("\n\nThis instantiation yields:\n\n");

            if (current.dependentTerms.Last() != null)
            {
                current.dependentTerms.Last().PrettyPrint(content, format);
            }
        }

        private static void legacyInstantiationInfo(InfoPanelContent content, PrettyPrintFormat format, Instantiation previous)
        {
            previous.printNoMatchdisclaimer(content);
            content.switchFormat(InfoPanelContent.SubtitleFont, Color.DarkMagenta);
            content.Append("\n\nBlamed terms:\n\n");
            content.switchToDefaultFormat();

            foreach (var t in previous.Responsible)
            {
                content.Append("\n");
                t.PrettyPrint(content, format);
                content.Append("\n\n");
            }

            content.Append('\n');
            content.switchToDefaultFormat();
            content.switchFormat(InfoPanelContent.SubtitleFont, Color.DarkMagenta);
            content.Append("Bound terms:\n\n");
            content.switchToDefaultFormat();
            foreach (var t in previous.Bindings)
            {
                content.Append("\n");
                t.PrettyPrint(content, format);
                content.Append("\n\n");
            }

            content.switchFormat(InfoPanelContent.SubtitleFont, Color.DarkMagenta);
            content.Append("Quantifier Body:\n\n");
        }

        public IEnumerable<Instantiation> getInstantiations()
        {
            return pathInstantiations;
        }
    }
}
