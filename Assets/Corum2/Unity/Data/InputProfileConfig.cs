using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [CreateAssetMenu(menuName = "Corum2/Input Profile", fileName = "InputProfile")]
    public sealed class InputProfileConfig : ScriptableObject
    {
        public MetaFields meta = new MetaFields { domainId = "input.profile", policyIds = new[] { "EXP-INPUT-01", "EXP-INPUT-02", "EXP-INPUT-03", "EXP-TIME-01" }, relatedRIds = new[] { "R-02-01", "R-03-01" } };
        [Range(0f, 0.95f)] public float stickDeadzone = 0.35f;
        [Tooltip("Press-to-press seconds on raw input time. 0.25 is the spec's test proposal.")]
        public float doubleTapWindow = 0.25f;
        [Tooltip("Stick bindings for movement (Vector2).")]
        public string[] moveStickPaths = { "<Gamepad>/leftStick" };
        [Tooltip("Keyboard movement composites: up,down,left,right per entry.")]
        public string[] moveKeyComposites = { "<Keyboard>/w,<Keyboard>/s,<Keyboard>/a,<Keyboard>/d", "<Keyboard>/upArrow,<Keyboard>/downArrow,<Keyboard>/leftArrow,<Keyboard>/rightArrow" };
        public List<ActionBinding> buttons = new List<ActionBinding>();

        public InputProfile ToCore() => new InputProfile { Meta = meta.ToCore(), StickDeadzone = stickDeadzone, DoubleTapWindow = doubleTapWindow };
    }
}
