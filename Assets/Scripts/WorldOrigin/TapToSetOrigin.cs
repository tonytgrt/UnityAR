using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace UnityAR
{
    /// <summary>
    /// Moves the <see cref="WorldOrigin"/> to where an interactor's beam hits a detected surface when the
    /// user taps. Detected surfaces are anything AR Foundation places under the XR Origin's trackables
    /// parent, such as environment meshes from an <c>ARMeshManager</c> and <c>ARPlane</c>s.
    /// </summary>
    /// <remarks>
    /// The phone controller's tap is its trigger, which the XRI default input actions bind to Activate;
    /// a tracked hand's pinch is Select. Either counts as a tap.
    /// </remarks>
    public class TapToSetOrigin : MonoBehaviour
    {
        [SerializeField]
        WorldOrigin m_WorldOrigin;

        [SerializeField]
        [Tooltip("Interactors whose beam can place the origin. Leave empty to use every Near-Far Interactor under the XR Origin.")]
        List<NearFarInteractor> m_Interactors = new List<NearFarInteractor>();

        readonly List<Collider> m_HitColliders = new List<Collider>();
        readonly List<RaycastHit> m_Hits = new List<RaycastHit>();

        void Start()
        {
            if (m_Interactors.Count == 0)
                m_Interactors.AddRange(m_WorldOrigin.xrOrigin.GetComponentsInChildren<NearFarInteractor>(true));
        }

        void Update()
        {
            foreach (var interactor in m_Interactors)
            {
                if (interactor == null || !interactor.isActiveAndEnabled || !WasTapped(interactor))
                    continue;

                if (TryGetSurfaceHit(interactor, out var point))
                    m_WorldOrigin.SetOrigin(point);
                else
                    Debug.Log("[WorldOrigin] Tap ignored: beam is not on a detected surface");
            }
        }

        static bool WasTapped(NearFarInteractor interactor)
        {
            return interactor.activateInput.ReadWasPerformedThisFrame() ||
                interactor.selectInput.ReadWasPerformedThisFrame();
        }

        bool TryGetSurfaceHit(NearFarInteractor interactor, out Vector3 point)
        {
            point = default;
            if (!(interactor.farInteractionCaster is CurveInteractionCaster caster))
                return false;

            m_HitColliders.Clear();
            if (!caster.TryGetColliderTargets(interactor.interactionManager, m_HitColliders, m_Hits) || m_Hits.Count == 0)
                return false;

            // Hits are sorted nearest first, so this is where the beam stops.
            var hit = m_Hits[0];
            if (!IsDetectedSurface(hit.collider))
                return false;

            point = hit.point;
            return true;
        }

        bool IsDetectedSurface(Collider collider)
        {
            return collider.transform.IsChildOf(m_WorldOrigin.xrOrigin.TrackablesParent);
        }
    }
}
