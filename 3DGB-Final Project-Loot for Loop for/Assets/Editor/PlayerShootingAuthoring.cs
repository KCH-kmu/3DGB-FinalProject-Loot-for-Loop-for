using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PlayerShootingAuthoring
{
    const string Models = "Assets/ThirdParty/ToonShooter/Characters/FBX/";
    const string Folder = "Assets/Gameplay/PlayerShooting";
    public const string Output = "../.utmp/PlayerShooting";
    public const string ControllerPath = "Assets/Gameplay/PlayerLocomotion.controller";

    static AnimationClip Source(string model, string motion) =>
        AssetDatabase.LoadAllAssetsAtPath(Models + model + ".fbx").OfType<AnimationClip>()
            .Single(c => c.name == "CharacterArmature|" + motion);

    [MenuItem("Tools/Gameplay/Player Shooting/Audit Compatibility")]
    public static void Audit()
    {
        var soldier = AssetDatabase.LoadAssetAtPath<GameObject>(Models + "Character_Soldier.fbx");
        var hazmat = AssetDatabase.LoadAssetAtPath<GameObject>(Models + "Character_Hazmat.fbx");
        var report = new StringBuilder("Soldier <- Hazmat generic-rig animation compatibility\n");
        foreach (string motion in new[] { "Walk_Shoot", "Run_Shoot" })
        {
            var clip = Source("Character_Hazmat", motion);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            int missing = 0; float maxPositionDifference = 0, maxAngle = 0, maxScaleDifference = 0;
            foreach (var binding in bindings)
            {
                var target = string.IsNullOrEmpty(binding.path) ? soldier.transform : soldier.transform.Find(binding.path);
                var source = string.IsNullOrEmpty(binding.path) ? hazmat.transform : hazmat.transform.Find(binding.path);
                if (!target || !source || (binding.type != typeof(Transform) && !target.GetComponent(binding.type)))
                { missing++; report.AppendLine("MISSING " + binding.path + " / " + binding.propertyName); continue; }
                maxPositionDifference = Mathf.Max(maxPositionDifference, Vector3.Distance(target.localPosition, source.localPosition));
                maxAngle = Mathf.Max(maxAngle, Quaternion.Angle(target.localRotation, source.localRotation));
                maxScaleDifference = Mathf.Max(maxScaleDifference, Vector3.Distance(target.localScale, source.localScale));
            }
            report.AppendLine($"{motion}: {clip.length:F3}s, {bindings.Length} curves, missing={missing}, max rest position delta={maxPositionDifference:F6}, angle={maxAngle:F4}, scale delta={maxScaleDifference:F6}");
            if (missing != 0 || maxPositionDifference > .001f || maxAngle > .1f || maxScaleDifference > .001f)
                throw new InvalidOperationException("Rig mismatch: " + report);
        }
        Directory.CreateDirectory(Output); File.WriteAllText(Output + "/compatibility.txt", report.ToString());
        Debug.Log("PLAYER_SHOOTING_COMPATIBILITY: PASS\n" + report);
    }

    [MenuItem("Tools/Gameplay/Player Shooting/Apply Soldier Shooting")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Use Edit mode.");
        Audit();
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Gameplay", "PlayerShooting");
        var idle = Copy("Character_Soldier", "Idle_Shoot");
        var walk = Copy("Character_Hazmat", "Walk_Shoot");
        var run = Copy("Character_Hazmat", "Run_Shoot");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller) throw new InvalidOperationException("Player controller missing.");
        Undo.RegisterCompleteObjectUndo(controller, "Add Soldier shooting states");
        foreach (string name in new[] { "Shooting", "Sprinting" })
            if (!controller.parameters.Any(p => p.name == name)) controller.AddParameter(name, AnimatorControllerParameterType.Bool);
        var machine = controller.layers[0].stateMachine;
        var idleState = machine.states.Single(s => s.state.name == "Idle").state;
        var runState = machine.states.Single(s => s.state.name == "Run").state;
        var idleShoot = State(machine, "Idle_Shoot", idle, new Vector3(480, 0));
        var walkShoot = State(machine, "Walk_Shoot", walk, new Vector3(480, 90));
        var runShoot = State(machine, "Run_Shoot", run, new Vector3(480, 180));
        var managed = new[] { idleState, runState, idleShoot, walkShoot, runShoot };
        foreach (var state in managed)
            foreach (var transition in state.transitions.ToArray())
                if (managed.Contains(transition.destinationState)) state.RemoveTransition(transition);
        foreach (var transition in machine.anyStateTransitions.ToArray())
            if (managed.Contains(transition.destinationState)) machine.RemoveAnyStateTransition(transition);
        Transition(machine, idleState, false, false);
        Transition(machine, runState, false, true);
        Transition(machine, idleShoot, true, false);
        Transition(machine, walkShoot, true, true, false);
        Transition(machine, runShoot, true, true, true);
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(machine);
        AssetDatabase.SaveAssets();
        Debug.Log("PLAYER_SHOOTING_APPLIED: Idle_Shoot / Walk_Shoot / Run_Shoot with mutually exclusive transitions.");
    }
    static AnimationClip Copy(string model, string motion)
    {
        string path = Folder + "/Soldier_" + motion + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!clip) { clip = Object.Instantiate(Source(model, motion)); AssetDatabase.CreateAsset(clip, path); }
        else EditorUtility.CopySerialized(Source(model, motion), clip);
        clip.name = "Soldier_" + motion;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip); return clip;
    }
    static AnimatorState State(AnimatorStateMachine machine, string name, AnimationClip clip, Vector3 position)
    {
        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        if (!state) state = machine.AddState(name, position);
        state.motion = clip; state.speed = 1; state.writeDefaultValues = true;
        EditorUtility.SetDirty(state); return state;
    }
    static void Transition(AnimatorStateMachine machine, AnimatorState target, bool shooting, bool moving, bool? sprinting = null)
    {
        var t = machine.AddAnyStateTransition(target);
        t.hasExitTime = false; t.hasFixedDuration = true; t.duration = .1f; t.canTransitionToSelf = false;
        t.AddCondition(shooting ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Shooting");
        t.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Moving");
        if (sprinting.HasValue) t.AddCondition(sprinting.Value ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Sprinting");
    }
}
