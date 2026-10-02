using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Woodsmen.Editor
{
    public class SetupDeathAnimationUtility
    {
        [MenuItem("Tools/Woodsmen/Setup Death Animations for Players")]
        public static void Run()
        {
            string[] paths = {
                "Assets/Players/Warrior/Animations/Warrior Animator.controller",
                "Assets/Players/Lumberjack/Animations/Lumberjack Animator.controller"
            };
            
            foreach (string path in paths)
            {
                AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null)
                {
                    Debug.LogWarning("[Woodsmen] Could not find Animator at " + path);
                    continue;
                }
                
                // 1. Add Parameters
                bool hasDeathTrigger = false;
                bool hasIsDeadBool = false;
                foreach (var param in controller.parameters)
                {
                    if (param.name == "Death") hasDeathTrigger = true;
                    if (param.name == "IsDead") hasIsDeadBool = true;
                }
                
                if (!hasDeathTrigger) controller.AddParameter("Death", AnimatorControllerParameterType.Trigger);
                if (!hasIsDeadBool) controller.AddParameter("IsDead", AnimatorControllerParameterType.Bool);
                
                // 2. Locate or Create Death State
                AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;
                AnimatorState deathState = null;
                AnimatorState locomotionState = rootStateMachine.defaultState;
                
                foreach (var state in rootStateMachine.states)
                {
                    if (state.state.name == "Death") deathState = state.state;
                    if (state.state.name == "Locomotion" || state.state.name == "Blend Tree") locomotionState = state.state;
                }
                
                if (deathState == null)
                {
                    deathState = rootStateMachine.AddState("Death");
                }
                
                // 3. AnyState -> Death Transition
                bool hasDeathTransition = false;
                foreach (var t in rootStateMachine.anyStateTransitions)
                {
                    if (t.destinationState == deathState) hasDeathTransition = true;
                }
                
                if (!hasDeathTransition)
                {
                    AnimatorStateTransition deathTransition = rootStateMachine.AddAnyStateTransition(deathState);
                    deathTransition.AddCondition(AnimatorConditionMode.If, 0, "Death");
                    deathTransition.duration = 0.15f;
                    deathTransition.hasExitTime = false;
                    deathTransition.canTransitionToSelf = false;
                }
                
                // 4. Death -> Locomotion (Revive) Transition
                if (locomotionState != null)
                {
                    bool hasReviveTransition = false;
                    foreach (var t in deathState.transitions)
                    {
                        if (t.destinationState == locomotionState) hasReviveTransition = true;
                    }
                    
                    if (!hasReviveTransition)
                    {
                        AnimatorStateTransition reviveTransition = deathState.AddTransition(locomotionState);
                        reviveTransition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsDead");
                        reviveTransition.duration = 0.25f;
                        reviveTransition.hasExitTime = false;
                    }
                }
                
                EditorUtility.SetDirty(controller);
                Debug.Log($"<color=#50fa7b>[Woodsmen]</color> Successfully wired Death & Revive logic in: {path}");
            }
            
            AssetDatabase.SaveAssets();
            Debug.Log("<color=#50fa7b>All Death animations set up! You can now assign your Death animations to the 'Death' state in the Animators.</color>");
        }
    }
}
