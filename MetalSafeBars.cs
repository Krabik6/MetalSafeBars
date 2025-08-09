using BepInEx;
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

        private readonly HashSet<GameObject> _seen = new HashSet<GameObject>();
        private float _lastLogTime = 0f;

        // Кэш ресурсов для фикса
        internal static Sprite WhiteSprite;
        internal static Material UnlitMat;
        internal static Material UiDefaultMat;

        // Тип HealthBar, найденный рефлексией
        private static Type _healthBarType;

        internal int SeenCount => _seen.Count;

        private void Awake()
        {
            Instance = this;
            Logger.LogInfo("[MetalSafeBars] Loaded");

            EnsureWhiteSprite();
            EnsureMaterials();
            ResolveHealthBarType();

            var go = new GameObject("MetalSafeBars_DebugOverlay");
            DontDestroyOnLoad(go);
            go.AddComponent<MetalSafeDebugOverlay>();

            StartCoroutine(ScanLoop());
        }

        private void ResolveHealthBarType()
        {
            // Прямо: "RoR2.UI.HealthBar, RoR2"
            _healthBarType = Type.GetType("RoR2.UI.HealthBar, RoR2");
            if (_healthBarType != null) return;

            // Подстраховка: ищем по всем загруженным сборкам
            try
            {
                var asms = AppDomain.CurrentDomain.GetAssemblies();
                foreach (var asm in asms)
                {
                    var t = asm.GetType("RoR2.UI.HealthBar");
                    if (t != null) { _healthBarType = t; break; }
                }
            }
            catch { }

            if (_healthBarType == null)
                Logger.LogWarning("[MetalSafeBars] Не удалось найти тип RoR2.UI.HealthBar — мод активен, но нечему патчиться.");
        }

        private void EnsureMaterials()
        {
            try { var sh = Shader.Find("Unlit/Color"); if (sh) UnlitMat = new Material(sh); } catch {}
            try { var sh = Shader.Find("UI/Default");   if (sh) UiDefaultMat = new Material(sh); } catch {}
        }

        private static void EnsureWhiteSprite()
        {
            if (WhiteSprite) return;
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            WhiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
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
            if (_healthBarType == null) return;

            var objects = Resources.FindObjectsOfTypeAll(_healthBarType) as UnityEngine.Object[];
            if (objects == null) return;

            foreach (var obj in objects)
            {
                var comp = obj as Component;
                if (!comp) continue;
                var go = comp.gameObject;
                if (!go || _seen.Contains(go)) continue;

                if (!go.activeInHierarchy) continue;

                // пропустим босс-бары по имени типа в родителях
                if (HasComponentTypeInParents(go, "BossHealthBar")) { _seen.Add(go); continue; }

                // Сторож, который постоянно «лечит» любые Image/TMP внутри
                var fixer = go.GetComponent<MetalSafeHealthbarFixer>();
                if (!fixer) fixer = go.AddComponent<MetalSafeHealthbarFixer>();
                fixer.Init(go, Logger);

                _seen.Add(go);
            }
        }

        private static bool HasComponentTypeInParents(GameObject go, string typeName)
        {
            var comps = go.GetComponentsInParent<Component>(true);
            foreach (var c in comps)
                if (c != null && c.GetType().Name == typeName) return true;
            return false;
        }
    }

    internal class MetalSafeDebugOverlay : MonoBehaviour
    {
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

    // СТОРОЖ: не рисует свой UI, только чинит материалы/спрайты у нативных картинок
    internal class MetalSafeHealthbarFixer : MonoBehaviour
    {
        private GameObject _root;
        private BepInEx.Logging.ManualLogSource _log;
        private float _nextScan;
        private int _lastChildrenHash;

        public void Init(GameObject root, BepInEx.Logging.ManualLogSource log)
        {
            _root = root;
            _log = log;
            ForcePatch("Init");
        }

        private void OnEnable() => ForcePatch("OnEnable");
        private void OnTransformChildrenChanged() => ForcePatch("ChildrenChanged");

        private void LateUpdate()
        {
            if (Time.time >= _nextScan)
            {
                _nextScan = Time.time + 0.33f;
                int hash = ChildrenHash();
                if (hash != _lastChildrenHash)
                {
                    ForcePatch("ChildrenHashChanged");
                    _lastChildrenHash = hash;
                }
                else
                {
                    PatchOnce(false);
                }
            }
        }

        private int ChildrenHash()
        {
            if (!_root) return 0;
            unchecked
            {
                int h = 17;
                var imgs = _root.GetComponentsInChildren<Image>(true);
                foreach (var i in imgs) if (i) h = h * 31 + i.GetInstanceID();
                var tmps = _root.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in tmps) if (t) h = h * 31 + t.GetInstanceID();
                return h;
            }
        }

        private void ForcePatch(string reason)
        {
            try
            {
                PatchOnce(true);
                _lastChildrenHash = ChildrenHash();
                _log?.LogInfo($"[MetalSafeBars] Patched HB ({reason})");
            }
            catch (Exception e)
            {
                _log?.LogError(e);
            }
        }

        private void PatchOnce(bool full)
        {
            if (!_root) return;

            // Images
            var images = _root.GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (!img) continue;

                if (!img.sprite && MetalSafeBarsPlugin.WhiteSprite)
                    img.sprite = MetalSafeBarsPlugin.WhiteSprite;

                if (MetalSafeBarsPlugin.UnlitMat) img.material = MetalSafeBarsPlugin.UnlitMat;
                else if (MetalSafeBarsPlugin.UiDefaultMat) img.material = MetalSafeBarsPlugin.UiDefaultMat;
                else img.material = null;

                img.maskable = false;
                img.useSpriteMesh = false;

                var c = img.color; if (c.a <= 0f) { c.a = 1f; img.color = c; }

                if (full && img.type == Image.Type.Filled)
                {
                    img.fillMethod = Image.FillMethod.Horizontal;
                    img.fillOrigin = (int)Image.OriginHorizontal.Left;
                }
            }

            // TMP
            var tmps = _root.GetComponentsInChildren<TMP_Text>(true);
            foreach (var t in tmps)
            {
                if (!t) continue;
                try
                {
                    if (t.font) t.fontMaterial = t.font.material;
                }
                catch { }
            }
        }
    }
}
