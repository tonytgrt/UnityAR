using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace UnityAR
{
    /// <summary>
    /// Reads tracked hand joints from XR Hands in world space.
    /// </summary>
    static class HandJoints
    {
        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        /// <summary>
        /// Finds the running hand tracking subsystem, or returns null if hand tracking isn't running.
        /// </summary>
        public static XRHandSubsystem FindRunningSubsystem()
        {
            SubsystemManager.GetSubsystems(s_Subsystems);
            foreach (var subsystem in s_Subsystems)
            {
                if (subsystem.running)
                    return subsystem;
            }
            return null;
        }

        /// <summary>
        /// Gets the world-space position of a joint of a tracked hand.
        /// </summary>
        public static bool TryGetWorldPosition(XROrigin xrOrigin, XRHand hand, XRHandJointID jointId, out Vector3 position)
        {
            position = default;
            if (!hand.isTracked || !hand.GetJoint(jointId).TryGetPose(out var pose))
                return false;

            // Joint poses are in the XR Origin's tracking space, the same space as the camera's pose.
            position = xrOrigin.CameraFloorOffsetObject.transform.TransformPoint(pose.position);
            return true;
        }
    }
}
