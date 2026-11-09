using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [CreateAssetMenu(menuName = "Corum2/Movement Config", fileName = "MovementConfig")]
    public sealed class MovementConfig : ScriptableObject
    {
        public MetaFields meta = new MetaFields { domainId = "movement.player", policyIds = new[] { "EXP-MOVE-01" }, relatedRIds = new[] { "R-03-01", "R-03-02", "R-03-04", "R-03-05", "R-03-06" } };

        [Header("Ground (m/s)")] public float walkSpeed = 3.5f;
        public float runSpeed = 7f;
        [Header("Jump")] public float gravity = 30f;
        public float jumpHeight = 2.2f;
        public float extraJumpHeight = 1.8f;
        public float airControlSpeed = 3.5f;
        public float airAcceleration = 18f;
        public float maxFallSpeed = 22f;
        [Header("Slide / Roll")] public float slideSpeed = 9f;
        public float slideDuration = 0.35f;
        public float rollSpeed = 7f;
        public float rollDuration = 0.4f;
        [Header("Air dash / Dive")] public float dashSpeed = 12f;
        public float dashDuration = 0.25f;
        public float dashEndSpeed = 3.5f;
        public float diveSpeed = 14f;
        public float diveAngleDeg = 45f;
        [Header("Body (m)")] public float bodyWidth = 0.6f;
        public float standHeight = 1.8f;
        public float crouchHeight = 0.9f;

        public MovementProfile ToCore() => new MovementProfile
        {
            Meta = meta.ToCore(),
            WalkSpeed = walkSpeed,
            RunSpeed = runSpeed,
            Gravity = gravity,
            JumpHeight = jumpHeight,
            ExtraJumpHeight = extraJumpHeight,
            AirControlSpeed = airControlSpeed,
            AirAcceleration = airAcceleration,
            MaxFallSpeed = maxFallSpeed,
            SlideSpeed = slideSpeed,
            SlideDuration = slideDuration,
            RollSpeed = rollSpeed,
            RollDuration = rollDuration,
            DashSpeed = dashSpeed,
            DashDuration = dashDuration,
            DashEndSpeed = dashEndSpeed,
            DiveSpeed = diveSpeed,
            DiveAngleDeg = diveAngleDeg,
            BodyWidth = bodyWidth,
            StandHeight = standHeight,
            CrouchHeight = crouchHeight,
        };
    }
}
