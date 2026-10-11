using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace UnityAR
{
    /// <summary>
    /// Finds where an interactor's beam hits detected real-world geometry.
    /// </summary>
    public static class BeamRaycast
    {
        static readonly List<Collider> s_HitColliders = new List<Collider>();
        static readonly List<RaycastHit> s_Hits = new List<RaycastHit>();

        /// <summary>
        /// Gets the point where the beam of <paramref name="interactor"/> stops, if that is on a detected surface.
        /// </summary>
        /// <remarks>
        /// Detected surfaces are anything AR Foundation places under the XR Origin's trackables parent, such as
        /// environment meshes and planes, and saved room meshes shown under an anchor.
        /// </remarks>
        /// <param name="interactor">The interactor whose far beam to test.</param>
        /// <param name="xrOrigin">The XR Origin that owns the trackables parent.</param>
        /// <param name="hit">The hit on the detected surface, if any.</param>
        /// <returns>Whether the beam's nearest hit is on a detected surface.</returns>
        public static bool TryGetDetectedSurfaceHit(NearFarInteractor interactor, XROrigin xrOrigin, out RaycastHit hit)
        {
            hit = default;
            if (!(interactor.farInteractionCaster is CurveInteractionCaster caster))
                return false;

            s_HitColliders.Clear();
            if (!caster.TryGetColliderTargets(interactor.interactionManager, s_HitColliders, s_Hits) || s_Hits.Count == 0)
                return false;

            // Hits are sorted nearest first, so this is where the beam stops.
            hit = s_Hits[0];
            return hit.collider.transform.IsChildOf(xrOrigin.TrackablesParent);
        }
    }
}
