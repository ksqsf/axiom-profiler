﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Z3AxiomProfiler.QuantifierModel
{
    public class BindingInfo
    {
        // Pattern used for this binding
        public Term fullPattern;

        // unmatched blame terms
        private readonly List<Term> unusedBlameTerms;

        // outstanding checks
        private readonly Dictionary<Term, List<Tuple<Term, List<List<Term>>>>> outstandingMatches = new Dictionary<Term, List<Tuple<Term, List<List<Term>>>>>();

        // bindings: freeVariable --> Term
        public readonly Dictionary<Term, Term> bindings = new Dictionary<Term, Term>();

        // highlighting info: Term --> List of path constraints
        public readonly Dictionary<Term, List<List<Term>>> matchContext = new Dictionary<Term, List<List<Term>>>();

        // equalities inferred from pattern matching
        // lower id is item1!
        public readonly Dictionary<Term, List<Term>> equalities = new Dictionary<Term, List<Term>>();


        public BindingInfo(Term pattern, ICollection<Term> blameTerms, ICollection<Term> bindings)
        {
            fullPattern = pattern;
            unusedBlameTerms = new List<Term>(blameTerms);
        }

        private BindingInfo(BindingInfo other)
        {
            bindings = new Dictionary<Term, Term>(other.bindings);
            matchContext = new Dictionary<Term, List<List<Term>>>(other.matchContext);
            equalities = new Dictionary<Term, List<Term>>(equalities);
            unusedBlameTerms = new List<Term>(other.unusedBlameTerms);
            fullPattern = other.fullPattern;
            outstandingMatches = new Dictionary<Term, List<Tuple<Term, List<List<Term>>>>>(other.outstandingMatches);
        }

        private BindingInfo clone()
        {
            return new BindingInfo(this);
        }

        public List<BindingInfo> allNextMatches(Term pattern)
        {
            if (pattern.id == -1)
            {
                // free var, do not expect to find a blameterm
                var copy = clone();
                copy.handleOutstandingMatches(pattern);
                return new List<BindingInfo> { copy };
            }
            return (from blameTerm in unusedBlameTerms
                    let copy = clone()
                    where copy.matchBlameTerm(pattern, blameTerm)
                    select copy)
                    .ToList();
        }

        private bool matchBlameTerm(Term pattern, Term matchTerm)
        {
            if (!matchCondition(pattern, matchTerm)) return false;
            unusedBlameTerms.Remove(matchTerm);

            // add blame term without context
            // context is provided by previous matches
            addOutstandingMatch(pattern, new Tuple<Term, List<List<Term>>>(matchTerm, new List<List<Term>>()));
            handleOutstandingMatches(pattern);
            return true;
        }

        private void handleOutstandingMatches(Term pattern)
        {
            // nothing outstanding
            if (!outstandingMatches.ContainsKey(pattern)) return;

            foreach (var termWithContext in outstandingMatches[pattern])
            {
                // outstanding term with its context
                var term = termWithContext.Item1;
                var context = termWithContext.Item2;

                addMatchContext(term, context);
                handleMatch(pattern, term);
            }
            outstandingMatches.Remove(pattern);
        }

        private void addMatchContext(Term term, List<List<Term>> context)
        {
            if (!matchContext.ContainsKey(term)) matchContext[term] = new List<List<Term>>();
            matchContext[term].AddRange(context);
        }

        private List<List<Term>> getContext(Term term)
        {
            if (!matchContext.ContainsKey(term)) matchContext[term] = new List<List<Term>>();
            return matchContext[term];
        }

        private void handleMatch(Term pattern, Term term)
        {
            if (bindings.ContainsKey(pattern) && bindings[pattern].id != term.id)
            {
                // already bound to something different!
                var currBinding = bindings[pattern];

                addEquality(pattern, currBinding);
            }

            bindings[pattern] = term;
            foreach (var subPatternWithSubTerm in pattern.Args.Zip(term.Args, Tuple.Create))
            {
                var subPattern = subPatternWithSubTerm.Item1;
                var subTerm = subPatternWithSubTerm.Item2;
                var outstandingItem = new Tuple<Term, List<List<Term>>>(subTerm, new List<List<Term>>());

                // build context for subterms
                if (getContext(term).Count == 0)
                {
                    outstandingItem.Item2.Add(new List<Term> { term });
                }
                else
                {
                    foreach (var copy in getContext(term).Select(history => new List<Term>(history) { term }))
                    {
                        outstandingItem.Item2.Add(copy);
                    }
                }

                addOutstandingMatch(subPattern, outstandingItem);
            }
        }

        private void addEquality(Term pattern, Term currBinding)
        {
            if (!equalities.ContainsKey(pattern)) equalities[pattern] = new List<Term>();
            equalities[pattern].Add(currBinding);
        }

        private void addOutstandingMatch(Term subPattern, Tuple<Term, List<List<Term>>> outstandingItem)
        {
            if (!outstandingMatches.ContainsKey(subPattern))
            {
                outstandingMatches[subPattern] = new List<Tuple<Term, List<List<Term>>>>();
            }
            outstandingMatches[subPattern].Add(outstandingItem);
        }

        private static bool matchCondition(Term pattern, Term term)
        {
            // id -1 signifies free variable
            // every term matches the free variable pattern
            if (pattern.id == -1) return true;
            return pattern.Name == term.Name &&
                   pattern.GenericType == term.GenericType &&
                   pattern.Args.Length == term.Args.Length;
        }

        public bool finalize(List<Term> blameTerms, List<Term> boundTerms)
        {
            if (unusedBlameTerms.Count != 0 ||
                bindings.Count != boundTerms.Count + blameTerms.Count) return false;

            foreach (var binding in bindings.Where( kvPair => kvPair.Key.id == -1))
            {
                var freeVar = binding.Key;
                var term = binding.Value;
                if (boundTerms.Any(bndTerm => bndTerm.id == term.id)) continue;
                
                // term bound to free var is not actually a bound term
                // do equality lookup to see whether there is an actually bound variable that is
                if (!fixBindingWithEqLookUp(boundTerms, term, freeVar)) return false;
            }
            return true;
        }

        private bool fixBindingWithEqLookUp(List<Term> boundTerms, Term term, Term freeVar)
        {
            var eqFound = false;
            foreach (var bndTerm in boundTerms.Where(bndTerm => recursiveEqualityLookUp(term, bndTerm)))
            {
                addEquality(freeVar, term);
                bindings[freeVar] = bndTerm;
                eqFound = true;
                break;
            }
            if (!eqFound) return false;
            return true;
        }

        private static bool recursiveEqualityLookUp(Term term1, Term term2)
        {
            // shortcut for comparing identical terms.
            if (term1.id == term2.id) return true;
            Term searchTerm;
            Term lookUpTerm;
            if (term1.dependentTerms.Count < term2.dependentTerms.Count)
            {
                searchTerm = term1;
                lookUpTerm = term2;
            }
            else
            {
                searchTerm = term2;
                lookUpTerm = term1;
            }

            // direct equality
            if (searchTerm.dependentTerms
                .Where(dependentTerm => dependentTerm.Name == "=")
                .Any(dependentTerm => dependentTerm.Args.Any(term => term.id == lookUpTerm.id)))
            {
                return true;
            }

            // no direct equality, check if prerequisites for recursive lookup are met. 
            if (searchTerm.Name != lookUpTerm.Name ||
                searchTerm.GenericType != lookUpTerm.GenericType ||
                searchTerm.Args.Length != lookUpTerm.Args.Length)
            {
                return false;
            }

            // do recursive lookup
            return searchTerm.Args.Zip(lookUpTerm.Args, Tuple.Create)
                .All(recursiveCompare => recursiveEqualityLookUp(recursiveCompare.Item1, recursiveCompare.Item2));
        }

        public List<Term> getDistinctBlameTerms()
        {
            return bindings
                .Where(bnd => bnd.Key.id != -1)
                .Select(bnd => bnd.Value)
                .Where(term => getContext(term).Count == 0)
                .ToList();
        }

        public List<KeyValuePair<Term, Term>> getBindingsToFreeVars()
        {
            return bindings
                .Where(bnd => bnd.Key.id == -1)
                .ToList();
        }
    }
}