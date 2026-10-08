using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace UnityAR
{
    /// <summary>
    /// Moves the <see cref="WorldOrigin"/> to where an interactor's beam hits a detected AR plane when the
    /// user taps.
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

                if (TryGetPlaneHit(interactor, out var point))
                    m_WorldOrigin.SetOrigin(point);
            }
        }

        static bool WasTapped(NearFarInteractor interactor)
        {
            return interactor.activateInput.ReadWasPerformedThisFrame() ||
                interactor.selectInput.ReadWasPerformedThisFrame();
        }

        bool TryGetPlaneHit(NearFarInteractor interactor, out Vector3 point)
        {
            point = default;
            if (!(interactor.farInteractionCaster is CurveInteractionCaster caster))
                return false;

            m_HitColliders.Clear();
            if (!caster.TryGetColliderTargets(interactor.interactionManager, m_HitColliders, m_Hits) || m_Hits.Count == 0)
                return false;

            // Hits are sorted nearest first, so this is where the beam stops.
            var hit = m_Hits[0];
            if (hit.collider.GetComponentInParent<ARPlane>() == null)
                return false;

            point = hit.point;
            return true;
        }
    }
}
