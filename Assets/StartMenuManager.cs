using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// Attach to an object in your title/start menu scene and assign the two buttons
// and the fade image in the Inspector.
public class StartMenuManager : MonoBehaviour
{
    [Header("Buttons")]
    public Button startButton;
    public Button creditsButton;

    [Header("Fade To White")]
    [Tooltip("Full-screen white Image, alpha 0. Left inactive until Start is clicked.")]
    public Image fadeImage;
        public GameObject fadeImageg;

    public float fadeDuration = 1.5f;

    [Header("Scenes")]
    public string tutorialSceneName = "Tutorial";
    public string creditsSceneName = "Credits";

    public GameObject openEyesImage;

    private void Start()
    {
        if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
        if (creditsButton != null) creditsButton.onClick.AddListener(OnCreditsClicked);

        if (fadeImage != null) fadeImage.gameObject.SetActive(false);
    }

    private void OnStartClicked()
    {
        openEyesImage.SetActive(true);
        fadeImageg.SetActive(true);

        // Prevent double-clicks from starting a second fade / firing another load.
        if (startButton != null) startButton.interactable = false;
        if (creditsButton != null) creditsButton.interactable = false;

        StartCoroutine(FadeToWhiteThenLoad());

    }

    private void OnCreditsClicked()
    {
        SceneManager.LoadScene(creditsSceneName);
    }

    private IEnumerator FadeToWhiteThenLoad()
    {
        fadeImage.gameObject.SetActive(true);

        Color c = fadeImage.color;
        c.a = 0f;
        fadeImage.color = c;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / fadeDuration;
            c.a = Mathf.Clamp01(t);
            fadeImage.color = c;
            yield return null;
        }

        SceneManager.LoadScene(tutorialSceneName);
    }
}