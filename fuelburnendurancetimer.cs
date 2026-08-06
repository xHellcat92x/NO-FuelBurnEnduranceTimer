// ============================================================
// HUD Extras: Fuel Burn Endurance Timer
// Made by Hellcat92
// Version: 3.1.0
// Date: 06 August 2026
// ============================================================

using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using TMPro;
using System.Reflection;

namespace FuelBurnHUD
{
    public enum RangeUnit
    {
        Km,
        Nm,
        Mile
    }

    [BepInPlugin("com.hellcat92.fuelburnhud", "Fuel Burn Endurance Timer", "3.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<bool> ModEnabled;

        public static ConfigEntry<bool> ShowFUEL;
        public static ConfigEntry<bool> ShowTIMEREM;
        public static ConfigEntry<bool> ShowFLOW;
        public static ConfigEntry<bool> ShowRNG;

        public static ConfigEntry<int> FontSize;

        // Recolour slider — default HUD green (#00FF00)
        public static ConfigEntry<Color> HudColor;

        public static ConfigEntry<bool> UseMetricUnits;

        public static ConfigEntry<RangeUnit> RangeUnits;

        public const int LockedHorizontalOffset = -310;
        public const int LockedVerticalOffset = -120;

        private void Awake()
        {
            Logger.LogInfo("Fuel Burn Endurance Timer 3.1.0 Loaded");

            ModEnabled = Config.Bind("General", "Enable Mod", true);

            ShowFUEL = Config.Bind("HUD", "Show FUEL", true);
            ShowTIMEREM = Config.Bind("HUD", "Show TIME REM", true);
            ShowFLOW = Config.Bind("HUD", "Show FLOW", true);
            ShowRNG = Config.Bind("HUD", "Show RNG", true);

            FontSize = Config.Bind("HUD", "Font Size", 16);

            // Default HUD green (#00FF00)
            HudColor = Config.Bind("HUD", "HUD Text Color", new Color(0f, 1f, 0f, 1f));

            UseMetricUnits = Config.Bind("HUD", "Use Metric Units", false);

            RangeUnits = Config.Bind("HUD", "Range Units", RangeUnit.Km);

            gameObject.AddComponent<FuelBurnWatcher>();
        }
    }

    public class FuelBurnWatcher : MonoBehaviour
    {
        private CombatHUD hud;
        private Aircraft aircraft;
        private Transform hudCenter;

        private FuelBurnController controller;

        private void Update()
        {
            var newHud = FindObjectOfType<CombatHUD>();
            if (newHud != hud)
            {
                hud = newHud;
                ResetInjection();
            }

            if (hud == null)
                return;

            if (aircraft != hud.aircraft)
            {
                aircraft = hud.aircraft;
                ResetInjection();
            }

            if (aircraft == null)
                return;

            var fh = SceneSingleton<FlightHud>.i;
            if (fh == null)
                return;

            var newCenter = fh.GetHUDCenter();
            if (newCenter != hudCenter)
            {
                hudCenter = newCenter;
                ResetInjection();
            }

            if (hudCenter == null)
                return;

            if (controller == null)
                InjectHUD();
        }

        private void ResetInjection()
        {
            if (controller != null)
            {
                Destroy(controller.gameObject);
                controller = null;
            }
        }

        private void InjectHUD()
        {
            GameObject go = new GameObject("FuelBurnHUD");
            go.transform.SetParent(hudCenter, false);

            controller = go.AddComponent<FuelBurnController>();
            controller.aircraft = aircraft;
        }
    }

    public class FuelBurnController : MonoBehaviour
    {
        public Aircraft aircraft;

        private TextMeshProUGUI fuelText;
        private TextMeshProUGUI timeRemText;
        private TextMeshProUGUI flowText;
        private TextMeshProUGUI rangeText;

        private float lastFuelKg;
        private float lastTime;
        private float flowKgPerSec;
        private bool hasSample;

        private float flashTimer = 0f;

        private const float KgToLb = 2.20462262f;

        private TextMeshProUGUI GetVanillaHUDText()
        {
            var hud = FindObjectOfType<CombatHUD>();
            if (hud == null)
                return null;

            var field = typeof(CombatHUD).GetField("targetInfo",
                BindingFlags.Instance | BindingFlags.NonPublic);

            return field?.GetValue(hud) as TextMeshProUGUI;
        }

        private void Start()
        {
            fuelText = CreateTMP("FUEL");
            timeRemText = CreateTMP("TIME REM");
            flowText = CreateTMP("FLOW");
            rangeText = CreateTMP("RNG");

            var vanillaHUD = GetVanillaHUDText();
            if (vanillaHUD != null)
            {
                var vanillaFont = vanillaHUD.font;

                // ⭐ One shared material for FUEL, FLOW, RNG
                var matSolid = new Material(vanillaHUD.fontSharedMaterial);

                // ⭐ One isolated material for TIME REM
                var matTime = new Material(vanillaHUD.fontSharedMaterial);

                fuelText.font = vanillaFont;
                timeRemText.font = vanillaFont;
                flowText.font = vanillaFont;
                rangeText.font = vanillaFont;

                fuelText.fontSharedMaterial = matSolid;
                flowText.fontSharedMaterial = matSolid;
                rangeText.fontSharedMaterial = matSolid;

                timeRemText.fontSharedMaterial = matTime;

                ApplyUserColor();
            }
        }

        private void ApplyUserColor()
        {
            Color userColor = Plugin.HudColor.Value;

            fuelText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, userColor);
            flowText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, userColor);
            rangeText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, userColor);

            // TIME REM starts in user colour too
            timeRemText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, userColor);
        }

        private TextMeshProUGUI CreateTMP(string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(this.transform, false);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;

            RectTransform rt = tmp.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(800f, 40f);

            return tmp;
        }

        private void LateUpdate()
        {
            if (!Plugin.ModEnabled.Value)
            {
                fuelText.enabled = false;
                timeRemText.enabled = false;
                flowText.enabled = false;
                rangeText.enabled = false;
                return;
            }

            if (aircraft == null)
                return;

            // Reapply user colour for non-critical lines
            ApplyUserColor();

            var tanks = aircraft.GetFuelTanks();
            if (tanks == null || tanks.Count == 0)
                return;

            float totalKg = 0f;
            foreach (var t in tanks)
                if (t != null) totalKg += t.fuelMass;

            float now = Time.time;

            if (lastTime == 0f)
            {
                lastTime = now;
                lastFuelKg = totalKg;
                flowKgPerSec = 0f;
                hasSample = false;
            }

            if (now - lastTime >= 1f)
            {
                float delta = lastFuelKg - totalKg;
                float dt = now - lastTime;

                lastTime = now;
                lastFuelKg = totalKg;

                if (delta > 0.01f && dt > 0f)
                {
                    flowKgPerSec = delta / dt;
                    hasSample = true;
                }
                else
                {
                    flowKgPerSec = 0f;
                    hasSample = false;
                }
            }

            float enduranceSec =
                (hasSample && flowKgPerSec > 0f && totalKg > 0f)
                ? totalKg / flowKgPerSec
                : 0f;

            int baseX = Plugin.LockedHorizontalOffset;
            int baseY = Plugin.LockedVerticalOffset;

            fuelText.fontSize = Plugin.FontSize.Value;
            timeRemText.fontSize = Plugin.FontSize.Value;
            flowText.fontSize = Plugin.FontSize.Value;
            rangeText.fontSize = Plugin.FontSize.Value;

            // ⭐ Collapse‑upwards layout
            int y = baseY;

            if (Plugin.ShowFUEL.Value)
            {
                fuelText.enabled = true;
                fuelText.rectTransform.anchoredPosition = new Vector2(baseX, y);
                y -= 20;
            }
            else fuelText.enabled = false;

            if (Plugin.ShowTIMEREM.Value)
            {
                timeRemText.enabled = true;
                timeRemText.rectTransform.anchoredPosition = new Vector2(baseX, y);
                y -= 20;
            }
            else timeRemText.enabled = false;

            if (Plugin.ShowFLOW.Value)
            {
                flowText.enabled = true;
                flowText.rectTransform.anchoredPosition = new Vector2(baseX, y);
                y -= 20;
            }
            else flowText.enabled = false;

            if (Plugin.ShowRNG.Value)
            {
                rangeText.enabled = true;
                rangeText.rectTransform.anchoredPosition = new Vector2(baseX, y);
            }
            else rangeText.enabled = false;

            bool metric = Plugin.UseMetricUnits.Value;

            // FUEL
            if (Plugin.ShowFUEL.Value)
            {
                if (metric)
                    fuelText.text = $"FUEL [{totalKg:0}] kg";
                else
                    fuelText.text = $"FUEL [{totalKg * KgToLb:0}] lb";
            }

            // ⭐ TIME REM — isolated critical colour logic
            if (Plugin.ShowTIMEREM.Value)
            {
                if (!hasSample || enduranceSec <= 0f)
                {
                    timeRemText.text = "TIME REM (--:--:--)";
                    timeRemText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, Plugin.HudColor.Value);
                }
                else
                {
                    int h = Mathf.FloorToInt(enduranceSec / 3600);
                    int m = Mathf.FloorToInt((enduranceSec % 3600) / 60);
                    int s = Mathf.FloorToInt(enduranceSec % 60);
                    timeRemText.text = $"TIME REM ({h}:{m:D2}:{s:D2})";

                    float minutes = enduranceSec / 60f;

                    if (minutes <= 1f)
                    {
                        flashTimer += Time.deltaTime * 4f;
                        bool flash = Mathf.FloorToInt(flashTimer) % 2 == 0;
                        Color c = flash ? Color.red : Color.yellow;
                        timeRemText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, c);
                    }
                    else if (minutes <= 5f)
                    {
                        timeRemText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.red);
                    }
                    else if (minutes <= 15f)
                    {
                        timeRemText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, Color.yellow);
                    }
                    else
                    {
                        timeRemText.fontSharedMaterial.SetColor(ShaderUtilities.ID_FaceColor, Plugin.HudColor.Value);
                    }
                }
            }

            // FLOW
            if (Plugin.ShowFLOW.Value)
            {
                if (!hasSample || flowKgPerSec <= 0f)
                {
                    flowText.text = metric ? "FLOW [--] kg/s" : "FLOW [--] lb/s";
                }
                else
                {
                    if (metric)
                        flowText.text = $"FLOW [{flowKgPerSec:0.0}] kg/s";
                    else
                        flowText.text = $"FLOW [{flowKgPerSec * KgToLb:0.0}] lb/s";
                }
            }

            // RANGE
            if (Plugin.ShowRNG.Value)
            {
                if (!hasSample || enduranceSec <= 0f || aircraft.rb == null)
                {
                    rangeText.text = "RNG ----";
                }
                else
                {
                    float gs = aircraft.rb.velocity.magnitude;
                    float meters = gs * enduranceSec;

                    switch (Plugin.RangeUnits.Value)
                    {
                        case RangeUnit.Km:
                            float km = meters / 1000f;
                            rangeText.text = $"RNG {km:0}km";
                            break;

                        case RangeUnit.Mile:
                            float mi = meters / 1609.34f;
                            rangeText.text = $"RNG {mi:0}mi";
                            break;

                        default: // Nm
                            float nm = meters / 1852f;
                            rangeText.text = $"RNG {nm:0}nm";
                            break;
                    }
                }
            }
        }
    }
}
