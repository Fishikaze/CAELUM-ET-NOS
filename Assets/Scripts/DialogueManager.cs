using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Attach this to a persistent object in your UI Canvas (e.g. an empty "DialogueManager" GameObject).
public class DialogueManager : MonoBehaviour
{
    public static DialogueManager Instance { get; private set; }

    [Header("UI References")]
    public GameObject dialogueBox;      // the panel that shows/hides
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI dialogueText;
    public Image portraitImage;

    [Header("Typewriter Settings")]
    public float charactersPerSecond = 40f;

    public bool IsDialogueActive { get; private set; }

    private DialogueLine[] currentLines;
    private int currentIndex;
    private bool isTyping;
    private Coroutine typeCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (dialogueBox != null)
            dialogueBox.SetActive(false);
    }

    private void Update()
    {
        if (!IsDialogueActive) return;

        // Left click OR the interact key advances/skips — matches how most
        // dialogue systems let you either click through or hold the same
        // interact button.
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.E))
        {
            AdvanceOrSkip();
        }
    }

    public void StartDialogue(DialogueLine[] lines)
    {
        if (lines == null || lines.Length == 0) return;

        currentLines = lines;
        currentIndex = 0;
        IsDialogueActive = true;

        if (dialogueBox != null)
            dialogueBox.SetActive(true);

        ShowLine(currentIndex);
    }

    private void ShowLine(int index)
    {
        DialogueLine line = currentLines[index];

        if (nameText != null)
            nameText.text = line.speakerName;

        if (portraitImage != null)
        {
            portraitImage.sprite = line.portrait;
            portraitImage.enabled = line.portrait != null;
        }

        if (typeCoroutine != null)
            StopCoroutine(typeCoroutine);

        typeCoroutine = StartCoroutine(TypeLine(line.text));
    }

    private IEnumerator TypeLine(string fullText)
    {
        isTyping = true;
        dialogueText.text = "";

        float delay = 1f / Mathf.Max(charactersPerSecond, 1f);

        for (int i = 0; i < fullText.Length; i++)
        {
            dialogueText.text += fullText[i];
            yield return new WaitForSeconds(delay);
        }

        isTyping = false;
        typeCoroutine = null;
    }

    private void AdvanceOrSkip()
    {
        if (isTyping)
        {
            // First click while typing: skip straight to the full line.
            if (typeCoroutine != null)
                StopCoroutine(typeCoroutine);

            dialogueText.text = currentLines[currentIndex].text;
            isTyping = false;
            typeCoroutine = null;
            return;
        }

        // Line is already fully shown: advance to the next one.
        currentIndex++;

        if (currentIndex >= currentLines.Length)
        {
            EndDialogue();
        }
        else
        {
            ShowLine(currentIndex);
        }
    }

    private void EndDialogue()
    {
        IsDialogueActive = false;
        currentLines = null;

        if (dialogueBox != null)
            dialogueBox.SetActive(false);
    }
}