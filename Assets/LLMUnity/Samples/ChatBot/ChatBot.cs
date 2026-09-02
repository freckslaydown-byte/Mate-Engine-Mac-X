using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Collections;
using System.Linq;

namespace LLMUnitySamples
{
    public class ChatBot : MonoBehaviour
    {
        [Header("Containers")]
        public Transform chatContainer;
        public Transform inputContainer;

        [Header("Colors & Font")]
        public Color playerColor = new Color32(81, 164, 81, 255);
        public Color aiColor = new Color32(29, 29, 73, 255);
        public Color fontColor = Color.white;
        public Font font;
        public int fontSize = 16;

        [Header("Bubble Layout")]
        public int bubbleWidth = 600;
        public float textPadding = 10f;
        public float bubbleSpacing = 10f;
        public float bottomPadding = 10f;
        public Sprite sprite;
        public Sprite roundedSprite16;
        public Sprite roundedSprite32;
        public Sprite roundedSprite64;

        [Header("LLM")]
        public AnthropicChatHandler llmCharacter;

        [Header("TTS")]
        public SoVITSTTSHandler ttsHandler;

        [Header("Input Settings")]
        public string inputPlaceholder = "Message me";

        [Header("Streaming Audio")]
        public AudioSource streamAudioSource;

        [Header("Click to Speak")]
        public Camera targetCamera;

        [Header("Bubble Materials")]
        public Material playerMaterial;         
        public Material aiMaterial;             
        [Header("Text Materials")]
        public Material playerTextMaterial;      
        public Material aiTextMaterial;        
        [Header("Scroll")]
        public ScrollRect scrollRect;           
        public bool autoScrollOnNewMessage = true;     
        public bool respectUserScroll = true;            

        [Header("History")]
        [Min(0)] public int maxMessages = 100;           
        public bool trimOnlyWhenAtBottom = true;       
        public bool enableOffscreenTrim = false;        

        [Header("Font Colors (per side)")]
        public Color playerFontColor = Color.white;
        public Color aiFontColor = Color.white;

        [Header("Rounded Sprite Radius")]
        [Range(0, 64)]
        public int cornerRadius = 16; 
        private bool layoutDirty;

        private InputBubble inputBubble;
        private List<Bubble> chatBubbles = new List<Bubble>();
        private bool blockInput = true;
        private BubbleUI playerUI, aiUI;
        private bool warmUpDone = false;
        private int lastBubbleOutsideFOV = -1;
        private int widgetSide = 1; // 0 = hug left (draw right), 1 = hug right (draw left)

        // AI bubble click-to-speak: maps bubble RectTransform to its text
        private readonly List<(RectTransform rt, System.Func<string> getText)> aiBubbleClickTargets = new();

        private Animator avatarAnimator;
        private Animator lastAvatarAnimator;
        private static readonly int isTalkingHash = Animator.StringToHash("isTalking");


        void Start()
        {
            avatarAnimator = GetComponent<Animator>();

            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cornerRadius <= 16) sprite = roundedSprite16;
            else if (cornerRadius <= 32) sprite = roundedSprite32;
            else sprite = roundedSprite64;

            playerUI = new BubbleUI
            {
                sprite = sprite,
                font = font,
                fontSize = fontSize,
                fontColor = playerFontColor,
                bubbleColor = playerColor,
                bottomPosition = 0,
                leftPosition = 0,
                textPadding = textPadding,
                bubbleOffset = bubbleSpacing,
                bubbleWidth = bubbleWidth,
                bubbleHeight = -1
            };

            aiUI = new BubbleUI
            {
                sprite = sprite,
                font = font,
                fontSize = fontSize,
                fontColor = aiFontColor,
                bubbleColor = aiColor,
                bottomPosition = 0,
                leftPosition = 1,
                textPadding = textPadding,
                bubbleOffset = bubbleSpacing,
                bubbleWidth = bubbleWidth,
                bubbleHeight = -1
            };

            Transform inputParent = inputContainer != null ? inputContainer : chatContainer;

            inputBubble = new InputBubble(inputParent, playerUI, "InputBubble", "Loading...", 4);
            inputBubble.AddSubmitListener(onInputFieldSubmit);
            inputBubble.AddValueChangedListener(onValueChanged);
            inputBubble.setInteractable(false);

            FindAvatarSmart();

            // Warmup: AnthropicChatHandler is always ready
            if (llmCharacter != null)
                llmCharacter.Warmup(WarmUpCallback);
            else
                WarmUpCallback();
        }

        void FindAvatarSmart()
        {
            Animator found = null;
            var loader = FindAnyObjectByType<VRMLoader>();
            if (loader != null)
            {
                var current = loader.GetCurrentModel();
                if (current != null) found = current.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a && a.gameObject.activeInHierarchy);
            }
            if (found == null)
            {
                var modelParent = GameObject.Find("Model");
                if (modelParent != null) found = modelParent.GetComponentsInChildren<Animator>(true).FirstOrDefault(a => a && a.gameObject.activeInHierarchy);
            }
            if (found == null)
            {
                var all = GameObject.FindObjectsByType<Animator>(FindObjectsInactive.Include);
                found = all.FirstOrDefault(a => a && a.isActiveAndEnabled);
            }
            if (found != avatarAnimator)
            {
                avatarAnimator = found;
                lastAvatarAnimator = avatarAnimator;
            }
        }

        void RefreshAvatarIfChanged()
        {
            if (avatarAnimator == null || lastAvatarAnimator == null || avatarAnimator != lastAvatarAnimator)
            {
                FindAvatarSmart();
            }
        }


        private void MarkLayoutDirty()
        {
            layoutDirty = true;
        }

        void OnDisable()
        {
            if (streamAudioSource != null && streamAudioSource.isPlaying)
            {
                streamAudioSource.Stop();
                streamAudioSource.volume = 1f; 
            }
            if (avatarAnimator != null) avatarAnimator.SetBool(isTalkingHash, false);
        }

        /// <summary>
        /// The horizontal side the whole chat widget hugs, based on where the
        /// model sits on screen, so the widget isn't cut off by the screen edge.
        /// 0 = anchored left (draws right), 1 = anchored right (draws left).
        /// </summary>
        int GetWidgetSide()
        {
            if (avatarAnimator == null) return widgetSide;
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return widgetSide;
            Vector3 vp = cam.WorldToViewportPoint(avatarAnimator.transform.position);
            if (vp.z <= 0f) return widgetSide; // behind the camera: keep current side
            return vp.x < 0.5f ? 0 : 1;
        }

        /// <summary>
        /// Repositions every bubble and the input field to hug the given side.
        /// Keeps each element's current position/size, flipping only the anchor
        /// and the sign of the horizontal offset.
        /// </summary>
        void ApplyWidgetSide(int side)
        {
            if (side == widgetSide) return;
            widgetSide = side;
            playerUI.leftPosition = side;
            aiUI.leftPosition = side;

            foreach (Bubble b in chatBubbles)
                ReAnchorToSide(b.GetRectTransform(), side);

            if (inputBubble != null)
                ReAnchorToSide(inputBubble.GetRectTransform(), side);
        }

        void ReAnchorToSide(RectTransform rt, int side)
        {
            if (rt == null) return;
            Vector2 ap = rt.anchoredPosition;
            rt.pivot = new Vector2(side, rt.pivot.y);
            rt.anchorMin = new Vector2(side, rt.anchorMin.y);
            rt.anchorMax = new Vector2(side, rt.anchorMax.y);
            ap.x = Mathf.Abs(ap.x) * (side == 1 ? -1f : 1f);
            rt.anchoredPosition = ap;
        }

        Bubble AddBubble(string message, bool isPlayerMessage)
        {
            int side = GetWidgetSide();
            if (side != widgetSide) ApplyWidgetSide(side);
            BubbleUI ui = isPlayerMessage ? playerUI : aiUI;
            ui.leftPosition = side;
            Bubble bubble = new Bubble(chatContainer, ui, isPlayerMessage ? "PlayerBubble" : "AIBubble", message);
            chatBubbles.Add(bubble);
            bubble.OnResize(MarkLayoutDirty);

            var image = bubble.GetRectTransform().GetComponentInChildren<Image>(true);
            if (image != null)
            {
                image.material = isPlayerMessage ? playerMaterial : aiMaterial;
            }
            var text = bubble.GetRectTransform().GetComponentInChildren<Text>(true);
            if (text != null)
            {
                Material m = isPlayerMessage ? playerTextMaterial : aiTextMaterial;
                if (m != null)
                {
                    text.material = m;
                }
            }

            // AI 气泡：注册到点击检测列表
            if (!isPlayerMessage && ttsHandler != null)
            {
                var rt = bubble.GetRectTransform();
                aiBubbleClickTargets.Add((rt, () => bubble.GetText()));
            }

            if (autoScrollOnNewMessage && (!respectUserScroll || IsAtBottom()))
            {
                StartCoroutine(ScrollToBottomNextFrame());
            }

            TrimHistoryIfNeeded();

            return bubble;
        }

        void TrimHistoryIfNeeded()
        {
            if (maxMessages <= 0) return;

            if (chatBubbles.Count > maxMessages)
            {
                if (!trimOnlyWhenAtBottom || IsAtBottom())
                {
                    int removeCount = chatBubbles.Count - maxMessages;
                    for (int i = 0; i < removeCount; i++)
                    {
                        chatBubbles[i].Destroy();
                    }
                    chatBubbles.RemoveRange(0, removeCount);
                    UpdateBubblePositions();
                }
            }
        }

        bool IsAtBottom(float tolerance = 0.01f)
        {
            if (scrollRect == null) return true; 
            return scrollRect.verticalNormalizedPosition <= tolerance;
        }

        void ShowLoadedMessages()
        {
            if (llmCharacter == null) return;
            var history = llmCharacter.chat;
            int start = maxMessages > 0 ? Mathf.Max(0, history.Count - maxMessages) : 0;
            for (int i = start; i < history.Count; i++)
                AddBubble(history[i].content, history[i].role == "user");
            StartCoroutine(ScrollToBottomNextFrame());
        }

        void onInputFieldSubmit(string newText)
        {
            inputBubble.ActivateInputField();
            if (blockInput || newText.Trim() == "" || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                StartCoroutine(BlockInteraction());
                return;
            }
            if (llmCharacter == null) return;
            blockInput = true;

            string message = inputBubble.GetText().Replace("\v", "\n");

            AddBubble(message, true);
            Bubble aiBubble = AddBubble("...", false);

            if (avatarAnimator != null) avatarAnimator.SetBool(isTalkingHash, true);

            llmCharacter.Chat(
                message,
                (reply) => { aiBubble.SetText(reply); layoutDirty = true; },
                () =>
                {
                    string finalText = aiBubble.GetText();
                    if (avatarAnimator != null) avatarAnimator.SetBool(isTalkingHash, false);
                    layoutDirty = true;

                    if (ttsHandler != null)
                        ttsHandler.Speak(finalText);

                    AllowInput();
                }
            );
            inputBubble.SetText("");
        }

        private IEnumerator FadeOutStreamAudio(float duration = 0.5f)
        {
            float startVolume = streamAudioSource.volume;

            while (streamAudioSource.volume > 0f)
            {
                streamAudioSource.volume -= startVolume * Time.deltaTime / duration;
                yield return null;
            }

            streamAudioSource.Stop();
            streamAudioSource.volume = startVolume; 
        }

        public void WarmUpCallback()
        {
            warmUpDone = true;
            inputBubble.SetPlaceHolderText(inputPlaceholder);
            AllowInput();
        }

        public void AllowInput()
        {
            blockInput = false;
            inputBubble.ReActivateInputField();
        }

        public void CancelRequests()
        {
            llmCharacter?.CancelRequests();
            AllowInput();
        }

        IEnumerator<string> BlockInteraction()
        {
            inputBubble.setInteractable(false);
            yield return null;
            inputBubble.setInteractable(true);
            inputBubble.MoveTextEnd();
        }

        void onValueChanged(string newText)
        {
            if (Input.GetKey(KeyCode.Return))
            {
                if (inputBubble.GetText().Trim() == "")
                    inputBubble.SetText("");
            }
        }

        public void UpdateBubblePositions()
        {
            float y = bottomPadding;
            for (int i = chatBubbles.Count - 1; i >= 0; i--)
            {
                Bubble bubble = chatBubbles[i];
                RectTransform childRect = bubble.GetRectTransform();
                childRect.anchoredPosition = new Vector2(childRect.anchoredPosition.x, y);

                if (enableOffscreenTrim)
                {
                    float containerHeight = chatContainer.GetComponent<RectTransform>().rect.height;
                    if (y > containerHeight && lastBubbleOutsideFOV == -1)
                        lastBubbleOutsideFOV = i;
                }

                y += bubble.GetSize().y + bubbleSpacing;
            }
            var contentRect = chatContainer.GetComponent<RectTransform>();
            contentRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, y + bottomPadding);
        }

        void Update()
        {
            RefreshAvatarIfChanged();

            // Keep the whole widget (input + all bubbles) on the same side as the model
            int side = GetWidgetSide();
            if (side != widgetSide) ApplyWidgetSide(side);

            if (!inputBubble.inputFocused() && warmUpDone)
            {
                inputBubble.ActivateInputField();
                StartCoroutine(BlockInteraction());
            }

            // AI 气泡点击检测
            if (ttsHandler != null && Input.GetMouseButtonDown(0))
            {
                Vector2 mousePos = Input.mousePosition;
                Camera cam = targetCamera != null ? targetCamera : Camera.main;
                for (int i = aiBubbleClickTargets.Count - 1; i >= 0; i--)
                {
                    var (rt, getText) = aiBubbleClickTargets[i];
                    if (rt == null) { aiBubbleClickTargets.RemoveAt(i); continue; }
                    if (RectTransformUtility.RectangleContainsScreenPoint(rt, mousePos, cam))
                    {
                        if (ttsHandler.IsPlaying) ttsHandler.Stop();
                        else ttsHandler.Speak(getText());
                        break;
                    }
                }
            }

            if (enableOffscreenTrim && lastBubbleOutsideFOV != -1)
            {
                for (int i = 0; i <= lastBubbleOutsideFOV; i++)
                {
                    chatBubbles[i].Destroy();
                }
                chatBubbles.RemoveRange(0, lastBubbleOutsideFOV + 1);
                lastBubbleOutsideFOV = -1;
                UpdateBubblePositions();
            }
        }

        public void ExitGame()
        {
            Debug.Log("Exit button clicked");
            Application.Quit();
        }

        IEnumerator ScrollToBottomNextFrame()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 0f; 
        }

        void OnValidate()
        {
            if (cornerRadius <= 16) sprite = roundedSprite16;
            else if (cornerRadius <= 32) sprite = roundedSprite32;
            else sprite = roundedSprite64;
        }

        void LateUpdate()
        {
            if (!layoutDirty) return;
            layoutDirty = false;

            UpdateBubblePositions();
            if (autoScrollOnNewMessage && (!respectUserScroll || IsAtBottom()))
            {
                if (scrollRect != null) scrollRect.verticalNormalizedPosition = 0f;
            }
        }

    }
}