﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Luminance.Common.StateMachines
{
    public class PushdownAutomata<TStateWrapper, TStateIdentifier> where TStateWrapper : IState<TStateIdentifier> where TStateIdentifier : struct
    {
        /// <summary>
        ///     Represents a framework for hijacking a transition's final state selection.
        ///     This is useful for allowing states to transition to something customized when its default transition condition has been triggered, without having to duplicate conditions many times.
        /// </summary>
        public record TransitionHijack(Func<TStateIdentifier?, TStateIdentifier?> SelectionHijackFunction, Action<TStateIdentifier?> HijackAction);

        /// <summary>
        ///     Represents a framework for a state transition's information.
        /// </summary>
        public record TransitionInfo(TStateIdentifier? NewState, bool RememberPreviousState, Func<bool> TransitionCondition, Action TransitionCallback = null);

        public PushdownAutomata(TStateWrapper initialState)
        {
            StateStack.Push(initialState);
            RegisterState(initialState);
        }

        /// <summary>
        ///     A collection of custom states that should be performed when a state is ongoing.
        /// </summary>
        public readonly Dictionary<TStateIdentifier, Action> StateBehaviors = [];

        /// <summary>
        ///     A list of hijack actions to perform during a state transition.
        /// </summary>
        public List<TransitionHijack> HijackActions = [];

        /// <summary>
        ///     A generalized registry of states with individualized data.
        /// </summary>
        public Dictionary<TStateIdentifier, TStateWrapper> StateRegistry = [];

        /// <summary>
        ///     The state stack for the automaton.
        /// </summary>
        public Stack<TStateWrapper> StateStack = new();

        protected Dictionary<TStateIdentifier, List<TransitionInfo>> transitionTable = [];

        /// <summary>
        ///     The current state of the automaton.
        /// </summary>
        public TStateWrapper CurrentState => StateStack.Peek();

        /// <summary>
        ///     The set of actions that should occur when a state is popped.
        /// </summary>
        public event Action<TStateWrapper> OnStatePop;

        /// <summary>
        ///     The set of actions that should occur when a state transition occurs.
        /// </summary>
        public event Action<bool> OnStateTransition;

        public void AddTransitionStateHijack(Func<TStateIdentifier?, TStateIdentifier?> hijackSelection, Action<TStateIdentifier?> hijackAction = null)
        {
            HijackActions.Add(new(hijackSelection, hijackAction));
        }

        public void PerformBehaviors()
        {
            if (StateBehaviors.TryGetValue(CurrentState.Identifier, out Action behavior))
                behavior();
        }

        public void PerformStateTransitionCheck()
        {
            if (!StateStack.Any() || !transitionTable.TryGetValue(CurrentState.Identifier, out List<TransitionInfo> value))
                return;

            List<TransitionInfo> potentialStates = value ?? [];
            List<TransitionInfo> transitionableStates = potentialStates.Where(s => s.TransitionCondition()).ToList();

            if (!transitionableStates.Any())
                return;

            TransitionInfo transition = transitionableStates.First();

            // Pop the previous state if it doesn't need to be remembered.
            if (!transition.RememberPreviousState && StateStack.TryPop(out var oldState))
            {
                OnStatePop?.Invoke(oldState);
                oldState.OnPopped();
            }

            // Perform the transition. If there's no state to transition to, simply work down the stack.
            TStateIdentifier? newState = transition.NewState;
            var usedHijackAction = HijackActions.FirstOrDefault(h => !h.SelectionHijackFunction(newState).Equals(newState));
            if (usedHijackAction is not null)
            {
                newState = usedHijackAction.SelectionHijackFunction(newState);
                usedHijackAction.HijackAction?.Invoke(newState);
            }
            if (newState is not null)
                StateStack.Push(StateRegistry[newState.Value]);

            // It is important this is called before the callback, as the most common use of this event is to reset commonly used variables, and that should
            // not occur after they've potentially been set in the callback.
            OnStateTransition?.Invoke(!transition.RememberPreviousState);

            // Access the callback, if one is used.
            transition.TransitionCallback?.Invoke();

            // Since a transition happened, recursively call Update again.
            // This allows for multiple state transitions to happen in a single frame if necessary.
            PerformStateTransitionCheck();
        }

        public void RegisterState(TStateWrapper state) => StateRegistry[state.Identifier] = state;

        public void RegisterStateBehavior(TStateIdentifier state, Action behavior)
        {
            StateBehaviors[state] = behavior;
        }

        public void RegisterTransition(TStateIdentifier initialState, TStateIdentifier? newState, bool rememberPreviousState, Func<bool> transitionCondition, Action transitionCallback = null)
        {
            // Initialize the list of transition states for the initial state if there aren't any yet.
            if (!transitionTable.ContainsKey(initialState))
                transitionTable[initialState] = [];

            // Add to the transition state list.
            transitionTable[initialState].Add(new(newState, rememberPreviousState, transitionCondition, transitionCallback));
        }
    }
}
