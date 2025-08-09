using BepInEx;
using RoR2;
using RoR2.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MetalSafeBars
{
    [BepInPlugin("me.metalfix.ror2.healthbars", "MetalSafeBars", "1.0.0")]
    public class MetalSafeBarsPlugin : BaseUnityPlugin
    {
        internal static MetalSafeBarsPlugin Instance;

        private readonly HashSet<HealthBar> _seen = new HashSet<HealthBar>();
        private float _lastLogTime = 0f;

        private static Sprite _whiteSprite;
        private static Material _unlitMat;    // жёсткий фикс: Unlit/Color
        private static Material _uiDefaultMat; // запасной вариант

        internal int SeenCount => _seen.Count;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo("[MetalSafeBars] Loaded");

            EnsureWhiteSprite();
            EnsureMaterials();

            // Наэкранный индикатор, чтобы видеть, что мод жив
            var go = new GameObject("MetalSafeBars_DebugOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<MetalSafeDebugOverlay>();

            StartCoroutine(ScanLoop());
        }

        private void EnsureMaterials()
        {
            try
            {
                var shUnlit = Shader.Find("Unlit/Color");
                if (shUnlit) _unlitMat = new Material(shUnlit);
            }
            catch { }

            try
            {
                var shUi = Shader.Find("UI/Default");
                if (shUi) _uiDefaultMat = new Material(shUi);
            }
            catch { }
        }

        private static void EnsureWhiteSprite()
        {
            if (_whiteSprite) return;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        private IEnumerator ScanLoop()
        {
            var wait = new WaitForSeconds(0.5f);
            while (true)
            {
                try
                {
                    AttachToExistingBars();

                    if (Time.time - _lastLogTime >= 10f)
                    {
                        Logger.LogInfo($"[MetalSafeBars] Активен, отслежено {_seen.Count} healthbar'ов");
                        _lastLogTime = Time.time;
                    }
                }
                catch (Exception e) { Logger.LogError(e); }

                yield return wait;
            }
        }

        private void AttachToExistingBars()
        {
            var bars = Resources.FindObjectsOfTypeAll<HealthBar>();
            foreach (var hb in bars)
            {
                if (hb == null || _seen.Contains(hb)) continue;

                // босс-бары пропускаем
                if (HasComponentTypeInParents(hb.gameObject, "BossHealthBar"))
                {
                    _seen.Add(hb);
                    continue;
                }

                if (!hb.isActiveAndEnabled || !hb.gameObject.activeInHierarchy) continue;

                DumpBar(hb);               // 1) лог диагностики
                HardPatchImages(hb);       // 2) ЖЕСТКИЙ фикс материалов/спрайтов
                PatchTMP(hb);              //    чинит невидимый TMP

                // 3) минимальный наш оверлей-всплеск (на 3 сек чтобы было видно глазами)
                var proxy = hb.gameObject.GetComponent<MetalSafeHealthbarProxy>();
                if (!proxy) proxy = hb.gameObject.AddComponent<MetalSafeHealthbarProxy>();
                proxy.Init(hb, Logger);

                _seen.Add(hb);
            }
        }

        private static bool HasComponentTypeInParents(GameObject go, string typeName)
        {
            var comps = go.GetComponentsInParent<Component>(true);
            foreach (var c in comps)
                if (c != null && c.GetType().Name == typeName)
                    return true;
            return false;
        }

        private void DumpBar(HealthBar hb)
        {
            try
            {
                string path = GetPath(hb.transform);
                Logger.LogInfo($"[MetalSafeBars] HB PATH: {path}");
                var imgs = hb.GetComponentsInChildren<Image>(true);
                foreach (var img in imgs)
                {
                    if (!img) continue;
                    string sh = img.material ? (img.material.shader ? img.material.shader.name : "NO_SHADER") : "NO_MATERIAL";
                    string spr = img.sprite ? img.sprite.name : "NULL_SPRITE";
                    Logger.LogInfo($"[MetalSafeBars]  IMG '{img.name}' shader='{sh}' sprite='{spr}' a={img.color.a:0.###} type={img.type}");
                }
                var tmps = hb.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in tmps)
                {
                    if (!t) continue;
                    string f = t.font ? t.font.name : "NULL_FONT";
                    string fm = t.fontMaterial ? (t.fontMaterial.shader ? t.fontMaterial.shader.name : "NO_SHADER") : "NO_MATERIAL";
                    Logger.LogInfo($"[MetalSafeBars]  TMP '{t.name}' font='{f}' matShader='{fm}'");
                }
            }
            catch { }
        }

        private static string GetPath(Transform t)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            while (t != null)
            {
                sb.Insert(0, "/" + t.name);
                t = t.parent;
            }
            return sb.ToString();
        }

        // Главный фикс: глушим все странные материалы → Unlit/Color + белый спрайт
        private void HardPatchImages(HealthBar hb)
        {
            var images = hb.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (!img) continue;

                // белый спрайт, чтобы Fill работал предсказуемо
                if (!_whiteSprite) EnsureWhiteSprite();
                if (img.sprite == null) img.sprite = _whiteSprite;

                // принудительный материал (избавляемся от шейдерных сюрпризов/стенсила/премульта)
                if (_unlitMat) img.material = _unlitMat;
                else if (_uiDefaultMat) img.material = _uiDefaultMat;
                else img.material = null;

                // выключаем маскируемость (уменьшаем влияние RectMask2D/Stencil)
                img.maskable = false;
                img.useSpriteMesh = false;

                // подстрахуем альфу
                var c = img.color;
                if (c.a <= 0f) { c.a = 1f; img.color = c; }
            }
        }

        private void PatchTMP(HealthBar hb)
        {
            var tmps = hb.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmps)
            {
                if (!t) continue;
                try
                {
                    if (t.font != null)
                        t.fontMaterial = t.font.material; // дефолтный SDF под этот шрифт
                }
                catch { }
            }
        }
    }

    // Индикатор "мод жив" (левый верх)
    internal class MetalSafeDebugOverlay : MonoBehaviour
    {
        private float _nextChatTime;

        private void Update()
        {
            if (Time.time >= _nextChatTime)
            {
                _nextChatTime = Time.time + 15f;
                try { Chat.AddMessage("<color=#5CFF5C>[MetalSafeBars]</color> мод активен"); }
                catch { }
            }
        }

        private void OnGUI()
        {
            var c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.Box(new Rect(10, 10, 260, 54), GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(new Rect(18, 16, 244, 20), "MetalSafeBars ON");
            int seen = MetalSafeBarsPlugin.Instance ? MetalSafeBarsPlugin.Instance.SeenCount : 0;
            GUI.Label(new Rect(18, 36, 244, 20), $"Tracked healthbars: {seen}");
            GUI.color = c;
        }
    }

    // Наш минимальный поверхностный бар, плюс 3-сек всплеск
    internal class MetalSafeHealthbarProxy : MonoBehaviour
    {
        private HealthBar _hb;
        private BepInEx.Logging.ManualLogSource _log;

        private GameObject _overlayRoot;
        private Image _hpImg;
        private Image _shieldImg;
        private TMP_Text _anyHpText;

        private float _debugFlashUntil = 0f;

        public void Init(HealthBar hb, BepInEx.Logging.ManualLogSource log)
        {
            _hb = hb;
            _log = log;

            if (_overlayRoot == null)
            {
                _overlayRoot = new GameObject("MetalSafeOverlay");
                _overlayRoot.transform.SetParent(_hb.transform, false);
                _overlayRoot.transform.SetAsLastSibling();

                _hpImg = NewBar("HP");
                _shieldImg = NewBar("Shield");

                _hpImg.color = new Color(0.3f, 1f, 0.3f, 0.95f);
                _shieldImg.color = new Color(0.3f, 0.9f, 1f, 0.6f);

                _shieldImg.transform.SetSiblingIndex(0);
                _hpImg.transform.SetSiblingIndex(1);

                _debugFlashUntil = Time.time + 3f;
            }

            _anyHpText = _hb.GetComponentInChildren<TMP_Text>(true);
        }

        private Image NewBar(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_overlayRoot.transform, false);
            var img = go.AddComponent<Image>();
            img.sprite = GetWhite();  // критично для Filled
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.raycastTarget = false;
            img.maskable = false;
            img.useSpriteMesh = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            return img;
        }

        private Sprite GetWhite()
        {
            var f = typeof(MetalSafeBarsPlugin).GetField("_whiteSprite", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (f != null)
            {
                var sp = f.GetValue(null) as Sprite;
                if (sp) return sp;
            }
            // запасной путь
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        private void LateUpdate()
        {
            if (_hb == null) return;

            if (_debugFlashUntil > Time.time)
            {
                ApplyFills(1f, 1f);
                return;
            }

            if (TryGetFractionsFromHealthComponent(out float hpFill, out float shieldFill))
            {
                ApplyFills(hpFill, shieldFill);
                return;
            }

            if (TryGetFractionsFromText(out float hpFill2, out float shieldFill2))
            {
                ApplyFills(hpFill2, shieldFill2);
            }
        }

        private void ApplyFills(float hp, float shield)
        {
            hp = Mathf.Clamp01(hp);
            shield = Mathf.Clamp01(shield);
            if (_hpImg) _hpImg.fillAmount = hp;
            if (_shieldImg) _shieldImg.fillAmount = Mathf.Max(shield, hp);
        }

        private bool TryGetFractionsFromHealthComponent(out float hpFill, out float shieldFill)
        {
            hpFill = 0f; shieldFill = 0f;
            var hc = _hb.GetComponentInParent<HealthComponent>();
            if (hc == null) return false;

            float full = Mathf.Max(1f, hc.fullCombinedHealth);
            float hp = Mathf.Max(0f, hc.health);
            float shield = Mathf.Max(0f, hc.shield);

            hpFill = hp / full;
            shieldFill = (hp + shield) / full;
            return true;
        }

        private bool TryGetFractionsFromText(out float hpFill, out float shieldFill)
        {
            hpFill = 0f; shieldFill = 0f;
            if (_anyHpText == null) return false;

            var s = _anyHpText.text;
            if (string.IsNullOrEmpty(s)) return false;

            var cleaned = System.Text.RegularExpressions.Regex.Replace(s, @"[^\d/]", "");
            var parts = cleaned.Split('/');
            if (parts.Length != 2) return false;

            if (!float.TryParse(parts[0], out float cur)) return false;
            if (!float.TryParse(parts[1], out float max) || max <= 0f) return false;

            hpFill = Mathf.Clamp01(cur / max);
            shieldFill = hpFill;
            return true;
        }
    }
}
