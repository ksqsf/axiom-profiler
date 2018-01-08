﻿using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using AxiomProfiler.CycleDetection;
using AxiomProfiler.PrettyPrinting;

namespace AxiomProfiler.QuantifierModel
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

        public IEnumerable<System.Tuple<System.Tuple<Quantifier, Term>, int>> Statistics()
        {
            return pathInstantiations.GroupBy(i => System.Tuple.Create(i.Quant, i.bindingInfo.fullPattern)).Select(group => System.Tuple.Create(group.Key, group.Count()));
        }

        private CycleDetection.CycleDetection cycleDetector;

        private bool hasCycle()
        {
            if (cycleDetector == null)
            {
                cycleDetector = new CycleDetection.CycleDetection(pathInstantiations, 3);
            }
            return cycleDetector.hasCycle();
        }

        public void InfoPanelText(InfoPanelContent content, PrettyPrintFormat format)
        {
            printCycleInfo(content, format);
            content.switchFormat(PrintConstants.TitleFont, PrintConstants.defaultTextColor);
            content.Append("Path explanation:");
            content.switchToDefaultFormat();
            content.Append("\n\nLength: " + Length()).Append('\n');
            printPreamble(content, false);

            var pathEnumerator = pathInstantiations.GetEnumerator();
            if (!pathEnumerator.MoveNext() || pathEnumerator.Current == null) return; // empty path
            var current = pathEnumerator.Current;

            // first thing
            content.switchToDefaultFormat();

            if (current.bindingInfo == null)
            {
                legacyInstantiationInfo(content, format, current);
            }
            else
            {
                printPathHead(content, format, current);
            }

            while (pathEnumerator.MoveNext() && pathEnumerator.Current != null)
            {
                // between stuff
                var previous = current;
                current = pathEnumerator.Current;
                if (current.bindingInfo == null)
                {
                    legacyInstantiationInfo(content, format, current);
                    continue;
                }
                printInstantiationWithPredecessor(content, format, current, previous, cycleDetector);
            }

            // Quantifier info for last in chain
            content.switchToDefaultFormat();
            content.Append("\n\nThis instantiation yields:\n\n");

            if (current.concreteBody != null)
            {
                current.concreteBody.PrettyPrint(content, format);
            }
        }

        private static void highlightGens(Term[] potGens, PrettyPrintFormat format, GeneralizationState generalization) 
        {
            foreach (var term in potGens)
            {
                if (term is Term && generalization.IsReplaced(term.id))
                {
                    term.highlightTemporarily(format, PrintConstants.generalizationColor);
                }
                else
                {
                    highlightGens(term.Args, format, generalization);
                }
            }
        }

        private static void printInstantiationWithPredecessor(InfoPanelContent content, PrettyPrintFormat format,
            Instantiation current, Instantiation previous, CycleDetection.CycleDetection cycDetect)
        {
            current.tempHighlightBlameBindTerms(format);
            var potGens = previous.concreteBody.Args;
            var generalization = cycDetect.getGeneralization();
            highlightGens(potGens, format, generalization);

            content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
            content.Append("\n\nThis instantiation yields:\n\n");
            content.switchToDefaultFormat();
            previous.concreteBody.PrettyPrint(content, format);

            // Other prerequisites:
            var otherRequiredTerms = current.bindingInfo.getDistinctBlameTerms()
                .FindAll(term => current.bindingInfo.equalities.Any(eq => current.bindingInfo.bindings[eq.Key] == term) ||
                        !previous.concreteBody.isSubterm(term)).ToList();
            if (otherRequiredTerms.Count > 0)
            {
                content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                content.Append("\n\nTogether with the following term(s):");
                content.switchToDefaultFormat();
                foreach (var distinctBlameTerm in otherRequiredTerms)
                {
                    content.Append("\n\n");
                    distinctBlameTerm.PrettyPrint(content, format);
                }
            }

            if (current.bindingInfo.equalities.Count > 0)
            {
                content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                content.Append("\n\nRelevant equalities:\n\n");
                content.switchToDefaultFormat();

                foreach (var equality in current.bindingInfo.equalities)
                {
                    current.bindingInfo.bindings[equality.Key].PrettyPrint(content, format);
                    foreach (var term in equality.Value)
                    {
                        content.Append("\n=\n");
                        term.PrettyPrint(content, format);
                    }
                    content.Append("\n\n");
                }

                current.bindingInfo.PrintEqualitySubstitution(content, format);
            }

            content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
            content.Append("\n\nApplication of ");
            content.Append(current.Quant.PrintName);
            content.switchToDefaultFormat();
            content.Append("\n\n");

            current.Quant.BodyTerm.PrettyPrint(content, format);
            format.restoreAllOriginalRules();
        }

        private static void printPathHead(InfoPanelContent content, PrettyPrintFormat format, Instantiation current)
        {
            content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
            content.Append("\nStarting from the following term(s):\n\n");
            content.switchToDefaultFormat();
            current.tempHighlightBlameBindTerms(format);
            foreach (var distinctBlameTerm in current.bindingInfo.getDistinctBlameTerms())
            {
                distinctBlameTerm.PrettyPrint(content, format);
                content.Append("\n\n");
            }

            if (current.bindingInfo.equalities.Count > 0)
            {
                content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                content.Append("\nRelevant equalities:\n\n");
                content.switchToDefaultFormat();

                foreach (var equality in current.bindingInfo.equalities)
                {
                    current.bindingInfo.bindings[equality.Key].PrettyPrint(content, format);
                    foreach (var t in equality.Value)
                    {
                        content.Append("\n=\n");
                        t.PrettyPrint(content, format);
                    }
                    content.Append("\n\n");
                }

                current.bindingInfo.PrintEqualitySubstitution(content, format);
            }

            content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
            content.Append("\nApplication of ");
            content.Append(current.Quant.PrintName);
            content.switchToDefaultFormat();
            content.Append("\n\n");

            current.Quant.BodyTerm.PrettyPrint(content, format);
            format.restoreAllOriginalRules();
        }

        private void printCycleInfo(InfoPanelContent content, PrettyPrintFormat format)
        {
            if (!hasCycle()) return;
            var cycle = cycleDetector.getCycleQuantifiers();
            content.switchFormat(PrintConstants.TitleFont, PrintConstants.warningTextColor);
            content.Append("Possible matching loop found!\n");
            content.switchToDefaultFormat();
            content.Append("Number of repetitions: ").Append(cycleDetector.getRepetiontions() + "\n");
            content.Append("Length: ").Append(cycle.Count + "\n");
            content.Append("Loop: ");
            content.Append(string.Join(" -> ", cycle.Select(quant => quant.PrintName)));
            content.Append("\n");

            printPreamble(content, true);

            content.switchFormat(PrintConstants.TitleFont, PrintConstants.defaultTextColor);
            content.Append("\n\nGeneralized Loop Iteration:\n\n");

            var generalizationState = cycleDetector.getGeneralization();
            var generalizedTerms = generalizationState.generalizedTerms;

            // print last yield term before printing the complete loop
            // to give the user a term to match the highlighted pattern to
            content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
            content.Append("\nStarting anywhere with the following term(s):\n\n");
            content.switchToDefaultFormat();
            var insts = cycleDetector.getCycleInstantiations().GetEnumerator();
            insts.MoveNext();
            
            var alreadyIntroducedGeneralizations = new HashSet<int>();

            printGeneralizedTermWithPrerequisites(content, format, generalizationState, alreadyIntroducedGeneralizations, generalizedTerms.First(), insts.Current, true, false);

            var count = 1;
            var loopYields = generalizedTerms.GetRange(1, generalizedTerms.Count - 1);
            foreach (var term in loopYields)
            {
                format.restoreAllOriginalRules();
                content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                content.Append("\nApplication of ");
                content.Append(insts.Current?.Quant.PrintName);
                content.switchToDefaultFormat();
                content.Append("\n\n");

                // print quantifier body with pattern
                insts.Current?.tempHighlightBlameBindTerms(format);
                insts.Current?.Quant.BodyTerm.PrettyPrint(content, format);
                content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                content.Append("\n\nThis yields:\n\n");
                content.switchToDefaultFormat();

                insts.MoveNext();
                printGeneralizedTermWithPrerequisites(content, format, generalizationState, alreadyIntroducedGeneralizations, term, insts.Current, false, count == loopYields.Count);
                count++;
            }
            format.restoreAllOriginalRules();
            content.Append("\n\n");
        }

        private static void PrintNewlyIntroducedGeneralizations(InfoPanelContent content, PrettyPrintFormat format, IEnumerable<Term> newlyIntroducedGeneralizations)
        {
            if (newlyIntroducedGeneralizations.Any())
            {
                var dependentGeneralizationLookup = newlyIntroducedGeneralizations.ToLookup(gen => gen.Args.Count() > 0);
                var hasIndependent = dependentGeneralizationLookup[false].Any();
                if (hasIndependent)
                {
                    content.Append("For any term(s) ");

                    var ordered = dependentGeneralizationLookup[false].OrderBy(gen => -gen.id);
                    ordered.First().PrettyPrint(content, format);
                    content.switchToDefaultFormat();
                    foreach (var gen in ordered.Skip(1))
                    {
                        content.Append(", ");
                        gen.PrettyPrint(content, format);
                        content.switchToDefaultFormat();
                    }
                }
                if (dependentGeneralizationLookup[true].Any())
                {
                    if (hasIndependent)
                    {
                        content.Append(" and corresponding term(s) ");
                    }
                    else
                    {
                        content.Append("For corresponding term(s) ");
                    }

                    var ordered = dependentGeneralizationLookup[true].OrderBy(gen => -gen.id);
                    ordered.First().PrettyPrint(content, format);
                    content.switchToDefaultFormat();
                    foreach (var gen in ordered.Skip(1))
                    {
                        content.Append(", ");
                        gen.PrettyPrint(content, format);
                        content.switchToDefaultFormat();
                    }
                }
                content.Append(":\n");
            }
        }

        private static void printGeneralizedTermWithPrerequisites(InfoPanelContent content, PrettyPrintFormat format,
            GeneralizationState generalizationState, ISet<int> alreadyIntroducedGeneralizations, Term term, Instantiation instantiation, bool first, bool last)
        {
            generalizationState.tmpHighlightGeneralizedTerm(format, term, last);

            var newlyIntroducedGeneralizations = term.GetAllGeneralizationSubterms()
                    .GroupBy(gen => gen.generalizationCounter).Select(group => group.First())
                    .Where(gen => !alreadyIntroducedGeneralizations.Contains(gen.generalizationCounter));
            PrintNewlyIntroducedGeneralizations(content, format, newlyIntroducedGeneralizations);
            alreadyIntroducedGeneralizations.UnionWith(newlyIntroducedGeneralizations.Select(gen => gen.generalizationCounter));

            term.PrettyPrint(content, format);
            content.Append("\n");

            if (generalizationState.assocGenBlameTerm.TryGetValue(term, out var otherRequirements))
            {
                var constantTermsLookup = otherRequirements.ToLookup(t => t.ContainsGeneralization());
                var setTems = constantTermsLookup[true];
                var constantTerms = constantTermsLookup[false];

                if (constantTerms.Count() > 0)
                {
                    content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                    content.Append("\nTogether with the following term(s):");
                    content.switchToDefaultFormat();

                    foreach (var req in constantTerms)
                    {
                        content.Append("\n\n");
                        generalizationState.tmpHighlightGeneralizedTerm(format, req, last);
                        req.PrettyPrint(content, format);
                    }
                    content.Append("\n");
                }

                if (setTems.Count() > 0)
                {
                    foreach (var req in setTems)
                    {
                        content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                        content.Append($"\nTogether with a set of terms generated by {req.Responsible.Quant.PrintName} ({(generalizationState.IsProducedByLoop(req) ? "" : "not ")}part of the current matching loop) with the shape:\n\n");
                        content.switchToDefaultFormat();

                        newlyIntroducedGeneralizations = req.GetAllGeneralizationSubterms()
                            .GroupBy(gen => gen.generalizationCounter).Select(group => group.First())
                            .Where(gen => !alreadyIntroducedGeneralizations.Contains(gen.generalizationCounter));
                        PrintNewlyIntroducedGeneralizations(content, format, newlyIntroducedGeneralizations);
                        alreadyIntroducedGeneralizations.UnionWith(newlyIntroducedGeneralizations.Select(gen => gen.generalizationCounter));

                        generalizationState.tmpHighlightGeneralizedTerm(format, req, last);
                        req.PrettyPrint(content, format);
                        content.Append("\n");
                    }
                }
            }

            var bindingInfo = last ? generalizationState.GetWrapAroundBindingInfo() : generalizationState.GetGeneralizedBindingInfo(term.dependentInstantiationsBlame.First());
            if (bindingInfo.equalities.Count > 0)
            {
                content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                content.Append("\nRelevant equalities:\n\n");
                content.switchToDefaultFormat();

                foreach (var equality in bindingInfo.equalities)
                {
                    bindingInfo.bindings[equality.Key].PrettyPrint(content, format);
                    foreach (var t in equality.Value)
                    {
                        content.Append("\n=\n");
                        t.PrettyPrint(content, format);
                    }
                    content.Append("\n\n");
                }

                bindingInfo.PrintEqualitySubstitution(content, format);
            }

            if (last)
            {
                content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                content.Append("\nGeneralizations in the Next Iteration:\n\n");
                content.switchToDefaultFormat();
                generalizationState.PrintGeneralizationsForNextIteration(content, format);

                content.switchFormat(PrintConstants.SubtitleFont, PrintConstants.sectionTitleColor);
                content.Append("\nBindings to Start Next Iteration:\n\n");
                content.switchToDefaultFormat();

                foreach (var binding in bindingInfo.getBindingsToFreeVars())
                {
                    content.Append(binding.Key.Name).Append(" will be bound to:\n");
                    binding.Value.PrettyPrint(content, format);
                    content.switchToDefaultFormat();
                    content.Append("\n\n");
                }
            }
        }

        private void printPreamble(InfoPanelContent content, bool withGen)
        {
            content.Append("\nHighlighted terms are ");
            content.switchFormat(PrintConstants.DefaultFont, PrintConstants.patternMatchColor);
            content.Append("matched");
            content.switchToDefaultFormat();
            content.Append(" or ");
            content.switchFormat(PrintConstants.DefaultFont, PrintConstants.equalityColor);
            content.Append("matched using equality");
            content.switchToDefaultFormat();
            content.Append(" or ");
            content.switchFormat(PrintConstants.DefaultFont, PrintConstants.blameColor);
            content.Append("blamed");
            content.switchToDefaultFormat();
            content.Append(" or ");
            content.switchFormat(PrintConstants.DefaultFont, PrintConstants.bindColor);
            content.Append("bound");
            content.switchToDefaultFormat();
            if (withGen)
            {
                content.Append(" or ");
                content.switchFormat(PrintConstants.DefaultFont, PrintConstants.generalizationColor);
                content.Append("generalized");
                content.switchToDefaultFormat();
                content.Append(".\n\"");
                content.switchFormat(PrintConstants.DefaultFont, PrintConstants.generalizationColor);
                content.Append(">...<");
                content.switchToDefaultFormat();
                content.Append("\" indicates that a generalization is hidden below the max term depth");
            }
            content.Append(".\n");
        }

        private static void legacyInstantiationInfo(InfoPanelContent content, PrettyPrintFormat format, Instantiation instantiation)
        {
            instantiation.printNoMatchdisclaimer(content);
            content.switchFormat(PrintConstants.SubtitleFont, Color.DarkMagenta);
            content.Append("\n\nBlamed terms:\n\n");
            content.switchToDefaultFormat();

            foreach (var t in instantiation.Responsible)
            {
                content.Append("\n");
                t.PrettyPrint(content, format);
                content.Append("\n\n");
            }

            content.Append('\n');
            content.switchToDefaultFormat();
            content.switchFormat(PrintConstants.SubtitleFont, Color.DarkMagenta);
            content.Append("Bound terms:\n\n");
            content.switchToDefaultFormat();
            foreach (var t in instantiation.Bindings)
            {
                content.Append("\n");
                t.PrettyPrint(content, format);
                content.Append("\n\n");
            }

            content.switchFormat(PrintConstants.SubtitleFont, Color.DarkMagenta);
            content.Append("Quantifier Body:\n\n");

            instantiation.concreteBody?.PrettyPrint(content, format);
        }

        public IEnumerable<Instantiation> getInstantiations()
        {
            return pathInstantiations;
        }

        public bool TryGetLoop(out IEnumerable<System.Tuple<Quantifier, Term>> loop)
        {
            loop = null;
            if (!hasCycle()) return false;
            loop = cycleDetector.getCycleInstantiations().Take(cycleDetector.getCycleQuantifiers().Count)
                .Select(inst => System.Tuple.Create(inst.Quant, inst.bindingInfo.fullPattern));
            return true;
        }

        public bool TryGetCyclePath(out InstantiationPath cyclePath)
        {
            cyclePath = null;
            if (!hasCycle()) return false;
            var cycleInstantiations = cycleDetector.getCycleInstantiations();
            cyclePath = new InstantiationPath();
            foreach (var inst in cycleInstantiations)
            {
                cyclePath.append(inst);
            }
            return true;
        }

        public int GetNumRepetitions()
        {
            return cycleDetector.getRepetiontions();
        }
    }
}