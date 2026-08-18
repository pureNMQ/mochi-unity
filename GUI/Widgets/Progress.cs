using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mochi.Unity.GUI
{
    [AddComponentMenu("Mochi.Unity/GUI/Progress")]
    [ExecuteAlways]
    public class Progress : MonoBehaviour
    {
        [SerializeField] private Image fillImage;
        [SerializeField] private float maxValue;
        [SerializeField] private float currentValue;

        [SerializeField] private bool useGradient = false;
        [SerializeField] private Gradient gradient;

        public float MaxValue
        {
            get => maxValue;
            set => maxValue = value;
        }
        public float CurrentValue
        {
            get => currentValue;
            set => currentValue = value;
        }
        public float FillAmount => currentValue / maxValue;

        private void Update()
        {
            if (fillImage == null) return;
            fillImage.fillAmount = currentValue / maxValue;
            if (useGradient)
            {
                fillImage.color = gradient.Evaluate(fillImage.fillAmount);
            }
        }
    }
}
