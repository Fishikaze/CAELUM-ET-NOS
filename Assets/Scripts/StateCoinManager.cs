using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class StateCoinManager : MonoBehaviour
{
    [SerializeField] private Image coinImage;
    [SerializeField] private Sprite platformingSprite;
    [SerializeField] private Sprite fightingSprite;
    [SerializeField] private PlayerCombat playerCombat;

    [SerializeField] private float flipDuration = 0.25f;

    private bool wasFighting;
    private bool flipping;

    private void Start()
    {
        wasFighting = playerCombat.enabled;
        coinImage.sprite = wasFighting ? fightingSprite : platformingSprite;
        coinImage.rectTransform.localScale = new Vector3(wasFighting ? -1f : 1f, 1f, 1f);
    }

    private void Update()
    {
        bool isFighting = playerCombat.enabled;

        if (isFighting != wasFighting && !flipping)
        {
            wasFighting = isFighting;
            StartCoroutine(Flip(isFighting));
        }
    }

    private IEnumerator Flip(bool fighting)
    {
        flipping = true;

        RectTransform rect = coinImage.rectTransform;

        float startRot = rect.localEulerAngles.y;
        const float peakAngle = 90f;

        float t = 0f;
        bool changed = false;

        while (t < flipDuration)
        {
            t += Time.deltaTime;
            float percent = Mathf.Clamp01(t / flipDuration);

            // Triangle wave: 0 -> 90 -> 0, so we only ever show edge-on, never the mirrored back face
            float relativeAngle = peakAngle - Mathf.Abs((percent * 2f - 1f) * peakAngle);
            rect.localEulerAngles = new Vector3(0f, startRot + relativeAngle, 0f);

            // Width naturally follows the rotation - 1 at flat, 0 at edge-on (90°)
            float width = Mathf.Cos(relativeAngle * Mathf.Deg2Rad);

            // Sign represents which "side" is showing: old side before the swap, new side after
            float sign = changed ? (fighting ? 1f : 1f) : (fighting ? 1f : 1f);
            rect.localScale = new Vector3(width * sign, 1f, 1f);

            // Swap sprite right at the edge-on pinch point, where scale.x is ~0 (invisible)
            if (!changed && percent >= 0.5f)
            {
                coinImage.sprite = fighting ? fightingSprite : platformingSprite;
                changed = true;
            }

            yield return null;
        }

        rect.localEulerAngles = new Vector3(0f, startRot, 0f);
        rect.localScale = new Vector3(fighting ? 1f : 1f, 1f, 1f);

        flipping = false;
    }
}