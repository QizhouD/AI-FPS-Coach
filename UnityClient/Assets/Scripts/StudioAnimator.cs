using UnityEngine;

namespace FpsAiCoach
{
    /// <summary>
    /// The only per-frame motion in the room: the scanning reticle spins while the screen is idle,
    /// and the header beacon breathes. Both use unscaled time so they keep running if the app ever
    /// pauses simulation.
    /// </summary>
    public sealed class StudioAnimator : MonoBehaviour
    {
        [Header("Scanning reticle")]
        [SerializeField] private Transform reticle;
        [SerializeField] private float reticleRotationSpeed = 22f;

        [Header("Status beacon")]
        [SerializeField] private Transform beacon;
        [SerializeField] private float pulseSpeed = 2f;
        [SerializeField] private float pulseAmount = 0.09f;

        private Vector3 beaconBaseScale = Vector3.one;

        public void Configure(
            Transform reticleTransform,
            float rotationSpeed,
            Transform beaconTransform,
            float beaconPulseSpeed,
            float beaconPulseAmount)
        {
            reticle = reticleTransform;
            reticleRotationSpeed = rotationSpeed;
            beacon = beaconTransform;
            pulseSpeed = beaconPulseSpeed;
            pulseAmount = beaconPulseAmount;
        }

        private void Awake()
        {
            if (beacon != null)
                beaconBaseScale = beacon.localScale;
        }

        private void Update()
        {
            if (reticle != null && reticle.gameObject.activeSelf)
            {
                reticle.Rotate(
                    Vector3.forward,
                    reticleRotationSpeed * Time.unscaledDeltaTime,
                    Space.Self);
            }

            if (beacon != null)
            {
                var pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * pulseAmount;
                beacon.localScale = beaconBaseScale * pulse;
            }
        }

        public void SetReticleVisible(bool visible)
        {
            if (reticle != null && reticle.gameObject.activeSelf != visible)
                reticle.gameObject.SetActive(visible);
        }
    }
}
