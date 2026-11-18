using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace NazarethPlusCity
{
    /// <summary>
    /// Faces a nearby wall, shows a section preview, and builds one section at a time for wood.
    /// </summary>
    [DefaultExecutionOrder(40)]
    [DisallowMultipleComponent]
    public class LadderBuilder : MonoBehaviour
    {
        public LadderSystemSettings settings;
        public GameObject sectionPrefab;
        public PlayerResources resources;

        readonly List<LadderStructure> _ladders = new List<LadderStructure>();
        readonly List<Material> _previewMaterials = new List<Material>();
        GameObject _preview;
        LadderPlacement _placement;
        Transform _ladderRoot;
        FirstPersonWalker _walker;
        LadderClimbController _climb;
        float _statusUntil;
        string _status;

        public LadderPlacement CurrentPlacement => _placement;
        public IReadOnlyList<LadderStructure> Ladders => _ladders;
        public bool HasPreview => _preview != null && _preview.activeSelf;

        void Awake()
        {
            _walker = GetComponent<FirstPersonWalker>();
            _climb = GetComponent<LadderClimbController>();
            if (resources == null)
                resources = GetComponent<PlayerResources>() ?? gameObject.AddComponent<PlayerResources>();
        }

        void Start()
        {
            EnsureRoot();
            RefreshKnown();
            InitializeResources();
            var hud = transform.Find("WalkHud");
            LadderHud.Ensure(hud);
            LadderHud.Refresh(hud, resources, default, settings != null ? settings.woodPerSection : 2, false);
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;
            if (!GameplayReady())
            {
                HidePreview();
                return;
            }

            RefreshKnown();
            HandleBuildInput();

            if (_climb != null && _climb.IsClimbing && _climb.ActiveLadder != null)
            {
                LadderStructure active = _climb.ActiveLadder;
                _placement = PlacementFromLadder(active);
                UpdatePreview(_placement);
                LadderHud.Refresh(transform.Find("WalkHud"), resources, _placement, settings != null ? settings.woodPerSection : 2, true);
            }
            else
            {
                _placement = EvaluatePlacement();
                UpdatePreview(_placement);
                LadderHud.Refresh(transform.Find("WalkHud"), resources, _placement, settings != null ? settings.woodPerSection : 2, false);
            }

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime > _statusUntil)
                _status = null;
        }

        public void InitializeResources()
        {
            if (resources == null || resources.initialized || settings == null)
                return;
            float average = PlayerResources.AverageClimbHeight();
            int wood = LadderMath.StartingWood(average, settings.sectionHeightMeters, settings.woodPerSection, settings.starterLadderBudget);
            resources.ConfigureStart(wood, settings.startingCoins);
            Debug.Log("Ladder starting resources: average climb " + average.ToString("0.00") + " m, section "
                + settings.sectionHeightMeters.ToString("0.00") + " m, wood " + wood + ", coins " + settings.startingCoins + ".");
        }

        public LadderPlacement EvaluatePlacement()
        {
            Transform eye = AimTransform();
            return LadderTargeting.Evaluate(eye, transform.position, settings, _ladders);
        }

        public bool TryBuildSection(out string reason)
        {
            reason = null;
            if (settings == null || sectionPrefab == null)
            {
                reason = "Ladder system is not configured.";
                return false;
            }

            // While climbing, always extend the ladder you are on.
            if (_climb != null && _climb.IsClimbing && _climb.ActiveLadder != null)
                return CommitSection(_climb.ActiveLadder, out reason);

            LadderPlacement placement = EvaluatePlacement();
            _placement = placement;
            if (!placement.Valid)
            {
                reason = placement.Reason;
                return false;
            }

            LadderStructure ladder = placement.Existing;
            if (ladder == null)
            {
                EnsureRoot();
                var go = new GameObject("Ladder");
                go.transform.SetParent(_ladderRoot, false);
                ladder = go.AddComponent<LadderStructure>();
                ladder.Configure(placement, settings, sectionPrefab);
                _ladders.Add(ladder);
            }
            else
            {
                ladder.climbHeight = placement.ClimbHeight;
                ladder.roofY = placement.RoofY;
                ladder.landingPoint = placement.LandingPoint;
                ladder.requiredSections = placement.RequiredSections;
            }

            if (!CommitSection(ladder, out reason))
                return false;

            // Start climbing as soon as a nearby section exists; no E press.
            if (_climb != null && !_climb.IsClimbing)
            {
                float mount = settings != null ? settings.mountDistanceMeters + 0.85f : 2.2f;
                Vector2 player = new Vector2(transform.position.x, transform.position.z);
                Vector2 baseFlat = new Vector2(ladder.BasePosition.x, ladder.BasePosition.z);
                if ((player - baseFlat).sqrMagnitude <= mount * mount)
                    _climb.BeginClimb(ladder, 0.05f, false);
                else
                    _climb.TryAutoMount();
            }

            return true;
        }

        bool CommitSection(LadderStructure ladder, out string reason)
        {
            reason = null;
            if (ladder == null)
            {
                reason = "No ladder to extend.";
                return false;
            }

            if (ladder.IsComplete)
            {
                reason = "This ladder already reaches the roof.";
                return false;
            }

            int cost = Mathf.Max(0, settings.woodPerSection);
            if (!resources.CanAffordWood(cost))
            {
                reason = "Not enough wood (" + cost + " needed).";
                return false;
            }

            if (!ladder.TryAddSection(settings, out reason))
                return false;

            if (!resources.TrySpendWood(cost))
            {
                ladder.RemoveLastSection();
                if (ladder.BuiltSections == 0)
                {
                    _ladders.Remove(ladder);
                    Destroy(ladder.gameObject);
                }

                reason = "Not enough wood.";
                return false;
            }

            _placement = PlacementFromLadder(ladder);
            _status = ladder.BuiltSections + " / " + ladder.requiredSections + " sections · " + cost + " wood per section";
            _statusUntil = Time.unscaledTime + 2.5f;
            if (_climb != null)
                _climb.NotifyLadderExtended(ladder);
            return true;
        }

        static LadderPlacement PlacementFromLadder(LadderStructure ladder)
        {
            if (ladder == null)
                return default;
            return new LadderPlacement
            {
                Valid = true,
                Building = ladder.building,
                BasePosition = ladder.BasePosition,
                WallNormal = ladder.wallNormal,
                Yaw = ladder.transform.eulerAngles.y,
                ClimbHeight = ladder.climbHeight,
                RoofY = ladder.roofY,
                LandingPoint = ladder.landingPoint,
                RequiredSections = ladder.requiredSections,
                SlotKey = ladder.slotKey,
                Existing = ladder
            };
        }

        void HandleBuildInput()
        {
            if (Keyboard.current == null || !Keyboard.current.fKey.wasPressedThisFrame)
                return;
            if (!TryBuildSection(out string reason) && !string.IsNullOrEmpty(reason))
            {
                _status = reason;
                _statusUntil = Time.unscaledTime + 2.2f;
                if (_walker != null)
                    _walker.ShowHint(reason, 2.2f);
            }
        }

        void UpdatePreview(LadderPlacement placement)
        {
            if (!placement.Valid)
            {
                HidePreview();
                return;
            }

            if (_preview == null)
                CreatePreview();
            if (_preview == null)
                return;

            int built = placement.Existing != null ? placement.Existing.BuiltSections : 0;
            if (built >= placement.RequiredSections)
            {
                HidePreview();
                return;
            }

            float dense = settings.sectionHeightMeters;
            float meshHeight = settings.sourceMeshHeightMeters > 0.1f
                ? settings.sourceMeshHeightMeters
                : dense / Mathf.Max(0.0001f, settings.denseRungScale);
            float used = LadderMath.SectionUseHeight(built, placement.RequiredSections, placement.ClimbHeight, dense);
            float y = placement.BasePosition.y + (placement.Existing != null ? placement.Existing.BuiltClimbHeight : 0f);
            _preview.SetActive(true);
            _preview.transform.SetPositionAndRotation(new Vector3(placement.BasePosition.x, y, placement.BasePosition.z), Quaternion.Euler(0f, placement.Yaw, 0f));
            var section = _preview.GetComponent<LadderSection>();
            if (section != null)
                section.ApplyHeightScale(meshHeight, used);
            else
                _preview.transform.localScale = new Vector3(1f, used / Mathf.Max(0.05f, meshHeight), 1f);
            ColorPreview(placement.Existing == null || !placement.Existing.IsComplete);
        }

        void CreatePreview()
        {
            if (sectionPrefab == null)
                return;
            _preview = Instantiate(sectionPrefab);
            _preview.name = "LadderSectionPreview";
            _preview.hideFlags = HideFlags.DontSave;
            foreach (var collider in _preview.GetComponentsInChildren<Collider>())
                collider.enabled = false;
            TintPreview(_preview);
        }

        void TintPreview(GameObject root)
        {
            _previewMaterials.Clear();
            var renderers = root.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].materials;
                for (int m = 0; m < mats.Length; m++)
                {
                    var mat = mats[m];
                    if (mat == null)
                        continue;
                    if (mat.HasProperty("_BaseColor"))
                    {
                        Color c = mat.GetColor("_BaseColor");
                        c.a = 0.45f;
                        mat.SetColor("_BaseColor", c);
                    }

                    mat.renderQueue = (int)RenderQueue.Transparent;
                    _previewMaterials.Add(mat);
                }

                renderers[i].materials = mats;
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        void ColorPreview(bool ok)
        {
            Color tint = ok ? new Color(0.55f, 0.85f, 0.45f, 0.45f) : new Color(0.9f, 0.35f, 0.25f, 0.45f);
            for (int i = 0; i < _previewMaterials.Count; i++)
            {
                if (_previewMaterials[i] != null && _previewMaterials[i].HasProperty("_BaseColor"))
                    _previewMaterials[i].SetColor("_BaseColor", tint);
            }
        }

        void HidePreview()
        {
            if (_preview != null)
                _preview.SetActive(false);
        }

        Transform AimTransform()
        {
            // Aim from the body/eye so first- and third-person both target the wall the player faces.
            if (_walker != null && _walker.eye != null)
                return _walker.eye;
            return transform;
        }

        bool GameplayReady()
        {
            var manager = ViewModeManager.Instance;
            if (manager != null)
            {
                if (!manager.GameplayAllowed)
                    return false;
                if (manager.Current == ExplorationMode.BirdsEye)
                    return false;
            }

            return _walker == null || (!_walker.InputBlocked && _walker.drive != WalkerDrive.Parked);
        }

        void EnsureRoot()
        {
            if (_ladderRoot != null)
                return;
            var city = GameObject.Find("Nazareth");
            Transform parent = city != null ? city.transform : null;
            var existing = parent != null ? parent.Find(LadderIds.RootName) : null;
            if (existing != null)
            {
                _ladderRoot = existing;
                return;
            }

            var root = new GameObject(LadderIds.RootName);
            if (parent != null)
                root.transform.SetParent(parent, false);
            _ladderRoot = root.transform;
        }

        void RefreshKnown()
        {
            _ladders.RemoveAll(l => l == null);
            var found = Object.FindObjectsByType<LadderStructure>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null && !_ladders.Contains(found[i]))
                    _ladders.Add(found[i]);
            }
        }

        void OnDestroy()
        {
            if (_preview != null)
                Destroy(_preview);
        }
    }
}
