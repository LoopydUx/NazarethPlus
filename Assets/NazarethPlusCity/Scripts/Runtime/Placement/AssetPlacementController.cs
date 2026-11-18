using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace NazarethPlusCity
{
    /// <summary>
    /// Places catalog objects on the city, then saves them immediately.
    /// Q and E rotate the preview. While that is active the bird camera does not also climb.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public class AssetPlacementController : MonoBehaviour
    {
        enum SessionState
        {
            Explore,
            Catalog,
            Placing,
            Selected
        }

        enum EditKind
        {
            Create,
            Move,
            Rotate
        }

        [Header("Assignment")]
        public AssetCatalog catalog;
        public CityCoordinateFrame coordinates;
        [Tooltip("Closed green wheelie bin. Required. If this is empty, placement reports the missing assignment and does not spawn a substitute.")]
        public GameObject binPrefab;

        [Header("Placement")]
        public float previewDistance = 2f;
        public float rotateDegreesPerSecond = 110f;
        public float maxSlopeDegrees = 32f;

        public IPlacementStore Store { get; set; }
        public bool WantsCursorFree => _state != SessionState.Explore;
        public bool CapturesRotateKeys => _state == SessionState.Placing;

        SessionState _state;
        EditKind _edit;
        string _editId;
        string _activeCatalogId;
        GameObject _editObject;
        GameObject _preview;
        readonly List<Material> _previewMaterials = new List<Material>();
        float _yaw;
        float _scale = 1f;
        Vector3 _lockedPosition;
        bool _confirmGate;
        bool _eatConfirmRelease;
        int _blockConfirmFrame = -1;
        bool _restored;
        bool _loaded;
        bool _loadBlocked;
        float _statusUntil;
        PlacementDocument _document;
        readonly HashSet<string> _spawnedIds = new HashSet<string>();
        readonly List<string> _problems = new List<string>();
        readonly List<RaycastResult> _uiHits = new List<RaycastResult>();
        PlacedAssetMarker _selected;

        Button _addButton;
        GameObject _catalogPanel;
        Transform _catalogList;
        Text _catalogMessage;
        GameObject _instruction;
        Text _reasonText;
        Text _helpText;
        GameObject _manage;
        Text _manageTitle;
        Text _loadStatus;
        FirstPersonWalker _walker;

        void Awake()
        {
            if (!Application.isPlaying)
                return;
            if (Store == null)
                Store = new JsonFilePlacementStore();
            _document = new PlacementDocument();
        }

        void OnEnable()
        {
            if (!Application.isPlaying)
                return;
            var hud = transform.Find("WalkHud");
            if (hud != null)
                PlacementHudBuilder.Ensure(hud, false);
            Bind();
        }

        void Start()
        {
            if (!Application.isPlaying)
                return;
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            yield return null;
            Physics.SyncTransforms();
            float waited = 0f;
            while (!CityReady() && waited < 5f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!CityReady())
            {
                Report("Placements were not spawned because the city, terrain, or asset catalog is not ready. Saved records were kept.");
            }
            else
            {
                RestoreSavedPlacements();
            }

            ShowProblems();
            RefreshHud();
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;
            SyncWalkerFlags();
            bool suppressConfirm = false;
            if (_eatConfirmRelease)
            {
                suppressConfirm = true;
                if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                    _eatConfirmRelease = false;
            }

            if (GameplayOn() && Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame)
                ToggleCatalog();
            if (_state == SessionState.Placing)
            {
                ApplyRotation();
                if (!suppressConfirm && ConfirmPressed() && !PointerOverUi())
                    Confirm();
            }
            else if (_state == SessionState.Explore && GameplayOn() && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !PointerOverUi())
            {
                SelectUnderCursor();
            }

            if (_loadStatus != null && _loadStatus.gameObject.activeSelf && Time.unscaledTime >= _statusUntil)
                _loadStatus.gameObject.SetActive(false);
            RefreshHud();
        }

        void LateUpdate()
        {
            if (!Application.isPlaying || _state != SessionState.Placing)
                return;
            var evaluation = Evaluate();
            ShowEvaluation(evaluation);
            MovePreview(evaluation);
        }

        public bool TryHandleEscape()
        {
            if (_state == SessionState.Explore)
                return false;
            if (_state == SessionState.Placing)
                CancelPlacement();
            else if (_state == SessionState.Catalog)
                CloseCatalog();
            else
                ClearSelection();
            return true;
        }

        public void RestoreSavedPlacements()
        {
            if (_restored)
                return;
            if (!LoadDocument())
            {
                _restored = true;
                return;
            }

            _restored = true;
            for (int i = 0; i < _document.placements.Count; i++)
            {
                if (!TrySpawnRecord(_document.placements[i], out string problem) && !string.IsNullOrEmpty(problem))
                    Report(problem);
            }
        }

        public void ReloadFromStore()
        {
            _restored = false;
            _loaded = false;
            _loadBlocked = false;
            _spawnedIds.Clear();
            RestoreSavedPlacements();
        }

        public bool TrySpawnRecord(PlacementRecord record, out string problem)
        {
            problem = null;
            if (record == null || string.IsNullOrEmpty(record.id))
            {
                problem = "A saved placement has no id. It was left in the file.";
                return false;
            }

            if (_spawnedIds.Contains(record.id))
            {
                problem = "Duplicate placement id " + record.id + " was kept and spawned only once.";
                return false;
            }

            if (FindMarker(record.id) != null)
            {
                _spawnedIds.Add(record.id);
                return true;
            }

            GameObject prefab = ResolvePrefab(record.catalogId, out string missing);
            if (prefab == null)
            {
                problem = "Kept saved placement " + record.id + ". " + missing;
                return false;
            }

            if (!TryFlat(record, out Vector3 flat, out string geoError))
            {
                problem = "Kept saved placement " + record.id + ". " + geoError;
                return false;
            }

            var footprint = prefab.GetComponent<BoxCollider>();
            float scale = record.scale > 0f ? record.scale : 1f;
            if (!PlacementGround.TryRestoreHeight(flat.x, flat.z, footprint, record.rotationY, scale, out float ground))
            {
                problem = "Kept saved placement " + record.id + " because the ground under it could not be resolved.";
                return false;
            }

            var instance = Instantiate(prefab, EnsureRoot());
            instance.name = (string.IsNullOrEmpty(record.assetType) ? "Asset" : record.assetType) + "_" + Short(record.id);
            float y = ground + record.groundOffset;
            instance.transform.SetPositionAndRotation(new Vector3(flat.x, y, flat.z), Quaternion.Euler(0f, record.rotationY, 0f));
            instance.transform.localScale = Vector3.one * scale;
            var marker = instance.GetComponent<PlacedAssetMarker>();
            if (marker == null)
                marker = instance.AddComponent<PlacedAssetMarker>();
            CopyToMarker(marker, record);
            _spawnedIds.Add(record.id);
            return true;
        }

        void ToggleCatalog()
        {
            if (_state == SessionState.Placing)
                return;
            if (_state == SessionState.Catalog)
                CloseCatalog();
            else
                OpenCatalog();
        }

        void OpenCatalog()
        {
            if (!GameplayOn())
                return;
            ClearSelection();
            _state = SessionState.Catalog;
            BuildCatalogRows();
            SyncWalkerFlags();
            ViewModeManager.Instance?.ApplyCursor();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            _walker?.HideHint();
            RefreshHud();
        }

        void CloseCatalog()
        {
            _state = SessionState.Explore;
            SyncWalkerFlags();
            ViewModeManager.Instance?.ApplyCursor();
            ViewModeManager.Instance?.ShowModeHint();
            RefreshHud();
        }

        void BuildCatalogRows()
        {
            if (_catalogList == null)
                return;
            for (int i = _catalogList.childCount - 1; i >= 0; i--)
                Destroy(_catalogList.GetChild(i).gameObject);
            if (_catalogMessage != null)
                _catalogMessage.text = "";
            if (catalog == null || catalog.entries == null || catalog.entries.Count == 0)
            {
                if (_catalogMessage != null)
                    _catalogMessage.text = "The asset catalog is not assigned on CityWalker.";
                Report("The asset catalog is not assigned on CityWalker. No substitute object was spawned.");
                return;
            }

            for (int i = 0; i < catalog.entries.Count; i++)
            {
                PlaceableAssetEntry entry = catalog.entries[i];
                if (entry == null)
                    continue;
                GameObject prefab = ResolvePrefab(entry.catalogId, out string missing);
                bool ready = prefab != null;
                string label = string.IsNullOrEmpty(entry.displayName) ? entry.catalogId : entry.displayName;
                if (!ready)
                    label += " — prefab not assigned";
                Button choice = PlacementHudBuilder.CreateChoice(_catalogList, entry.thumbnail, label, ready);
                string id = entry.catalogId;
                choice.onClick.AddListener(() => BeginCreate(id));
                if (!ready)
                {
                    if (_catalogMessage != null)
                        _catalogMessage.text = missing;
                    Report(missing);
                }
            }
        }

        void BeginCreate(string catalogId)
        {
            if (ResolvePrefab(catalogId, out string missing) == null)
            {
                Report(missing);
                return;
            }

            _edit = EditKind.Create;
            _editId = null;
            _editObject = null;
            _activeCatalogId = catalogId;
            _yaw = _walker != null ? _walker.Heading : 0f;
            _scale = ScaleOf(catalogId);
            BeginPlacement();
        }

        void BeginMove()
        {
            if (_selected == null)
                return;
            _edit = EditKind.Move;
            _editId = _selected.placementId;
            _editObject = _selected.gameObject;
            _activeCatalogId = _selected.catalogId;
            _yaw = _selected.rotationY;
            _scale = _selected.scale > 0f ? _selected.scale : 1f;
            SetShown(_editObject, false);
            BeginPlacement();
        }

        void BeginRotate()
        {
            if (_selected == null)
                return;
            _edit = EditKind.Rotate;
            _editId = _selected.placementId;
            _editObject = _selected.gameObject;
            _activeCatalogId = _selected.catalogId;
            _yaw = _selected.rotationY;
            _scale = _selected.scale > 0f ? _selected.scale : 1f;
            _lockedPosition = _selected.transform.position;
            SetShown(_editObject, false);
            BeginPlacement();
        }

        void BeginPlacement()
        {
            _state = SessionState.Placing;
            _selected = null;
            _blockConfirmFrame = Time.frameCount;
            _eatConfirmRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;
            CreatePreview();
            SyncWalkerFlags();
            ViewModeManager.Instance?.ApplyCursor();
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);
            _walker?.HideHint();
            RefreshHud();
        }

        void CancelPlacement()
        {
            DestroyPreview();
            if (_editObject != null)
                SetShown(_editObject, true);
            _editObject = null;
            _editId = null;
            _state = SessionState.Explore;
            SyncWalkerFlags();
            ViewModeManager.Instance?.ApplyCursor();
            ViewModeManager.Instance?.ShowModeHint();
            RefreshHud();
        }

        void Confirm()
        {
            if (_confirmGate || _state != SessionState.Placing)
                return;
            if (Time.frameCount <= _blockConfirmFrame || _eatConfirmRelease)
                return;
            _confirmGate = true;
            try
            {
                PlacementEvaluation evaluation = Evaluate();
                ShowEvaluation(evaluation);
                MovePreview(evaluation);
                if (!evaluation.Valid)
                    return;
                if (_edit == EditKind.Create)
                    CommitCreate(evaluation);
                else
                    CommitEdit(evaluation);
            }
            finally
            {
                _confirmGate = false;
            }
        }

        void CommitCreate(PlacementEvaluation evaluation)
        {
            if (!LoadDocument())
                return;
            if (!TryGeographic(evaluation.Position, out double longitude, out double latitude, out string error))
            {
                Report(error);
                return;
            }

            var record = new PlacementRecord
            {
                id = Guid.NewGuid().ToString("D"),
                cityId = CityId(),
                assetType = TypeOf(_activeCatalogId),
                catalogId = _activeCatalogId,
                longitude = longitude,
                latitude = latitude,
                groundOffset = 0f,
                rotationY = Wrap(evaluation.Yaw),
                scale = _scale > 0f ? _scale : 1f,
                createdUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", System.Globalization.CultureInfo.InvariantCulture)
            };
            if (FindRecord(record.id) != null)
                return;
            _document.placements.Add(record);
            if (!SaveDocument())
            {
                _document.placements.Remove(record);
                return;
            }

            if (!TrySpawnRecord(record, out string problem) && !string.IsNullOrEmpty(problem))
                Report(problem);
            FinishPlacement();
        }

        void CommitEdit(PlacementEvaluation evaluation)
        {
            if (!LoadDocument())
                return;
            PlacementRecord record = FindRecord(_editId);
            if (record == null)
            {
                Report("The selected placement is no longer in the save file. It was not changed.");
                CancelPlacement();
                return;
            }

            if (!TryGeographic(evaluation.Position, out double longitude, out double latitude, out string error))
            {
                Report(error);
                return;
            }

            PlacementRecord backup = record.Copy();
            record.longitude = longitude;
            record.latitude = latitude;
            record.groundOffset = 0f;
            record.rotationY = Wrap(evaluation.Yaw);
            record.scale = _scale > 0f ? _scale : 1f;
            if (!SaveDocument())
            {
                CopyFields(backup, record);
                return;
            }

            if (_editObject != null)
            {
                _editObject.transform.SetPositionAndRotation(evaluation.Position, Quaternion.Euler(0f, record.rotationY, 0f));
                _editObject.transform.localScale = Vector3.one * record.scale;
                var marker = _editObject.GetComponent<PlacedAssetMarker>();
                if (marker != null)
                    CopyToMarker(marker, record);
                SetShown(_editObject, true);
            }

            _editObject = null;
            FinishPlacement();
        }

        void FinishPlacement()
        {
            DestroyPreview();
            _editId = null;
            _state = SessionState.Explore;
            SyncWalkerFlags();
            ViewModeManager.Instance?.ApplyCursor();
            ViewModeManager.Instance?.ShowModeHint();
            RefreshHud();
        }

        void DeleteSelected()
        {
            if (_selected == null)
                return;
            string id = _selected.placementId;
            if (!LoadDocument())
                return;
            PlacementRecord record = FindRecord(id);
            if (record == null)
            {
                Discard(_selected.gameObject);
                _selected = null;
                _state = SessionState.Explore;
                RefreshHud();
                return;
            }

            _document.placements.Remove(record);
            if (!SaveDocument())
            {
                _document.placements.Add(record);
                return;
            }

            _spawnedIds.Remove(id);
            Discard(_selected.gameObject);
            _selected = null;
            _state = SessionState.Explore;
            SyncWalkerFlags();
            ViewModeManager.Instance?.ApplyCursor();
            RefreshHud();
        }

        void SelectUnderCursor()
        {
            Camera camera = ActiveCamera();
            if (camera == null)
                return;
            Ray ray = Cursor.lockState == CursorLockMode.Locked
                ? camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 400f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                ClearSelection();
                return;
            }

            var marker = hit.collider.GetComponentInParent<PlacedAssetMarker>();
            if (marker == null)
            {
                ClearSelection();
                return;
            }

            _selected = marker;
            _state = SessionState.Selected;
            if (_manageTitle != null)
                _manageTitle.text = string.IsNullOrEmpty(marker.assetType) ? "Placed object" : marker.assetType;
            SyncWalkerFlags();
            ViewModeManager.Instance?.ApplyCursor();
            _walker?.HideHint();
            RefreshHud();
        }

        void ClearSelection()
        {
            if (_state != SessionState.Selected && _selected == null)
                return;
            _selected = null;
            if (_state == SessionState.Selected)
                _state = SessionState.Explore;
            SyncWalkerFlags();
            ViewModeManager.Instance?.ApplyCursor();
            ViewModeManager.Instance?.ShowModeHint();
            RefreshHud();
        }

        PlacementEvaluation Evaluate()
        {
            PlacementAimSample aim = _edit == EditKind.Rotate
                ? new PlacementAimSample { HasPoint = true, Point = _lockedPosition }
                : SampleAim();
            GameObject prefab = ResolvePrefab(_activeCatalogId, out _);
            return PlacementValidator.Evaluate(new PlacementCheck
            {
                Aim = aim,
                LockPosition = _edit == EditKind.Rotate,
                LockedPosition = _lockedPosition,
                YawDegrees = _yaw,
                Scale = _scale,
                Footprint = prefab != null ? prefab.GetComponent<BoxCollider>() : null,
                Boundary = FindAnyObjectByType<CityBoundaryMap>(),
                MaxSlopeDegrees = maxSlopeDegrees,
                PlayerPosition = _walker != null ? _walker.transform.position : Vector3.zero,
                PlayerRadius = _walker != null ? _walker.capsuleRadius : 0.3f,
                CheckPlayer = _walker != null,
                Preview = _preview != null ? _preview.transform : null,
                IgnoreInstance = _editObject != null ? _editObject.transform : null
            });
        }

        PlacementAimSample SampleAim()
        {
            if (IsBird)
                return PlacementGround.CursorSample(ActiveCamera(), _preview != null ? _preview.transform : null, _editObject != null ? _editObject.transform : null);
            if (_walker == null)
                return new PlacementAimSample { Reason = "The walker is missing." };
            Vector3 forward = _walker.transform.forward;
            if (!IsBird && ViewModeManager.Instance != null && ViewModeManager.Instance.Current == ExplorationMode.ThirdPerson && _walker.cameraHeading != null)
                forward = _walker.cameraHeading.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.forward;
            return new PlacementAimSample
            {
                HasPoint = true,
                Point = _walker.transform.position + forward.normalized * previewDistance
            };
        }

        void ApplyRotation()
        {
            if (Keyboard.current == null)
                return;
            float direction = 0f;
            if (Keyboard.current.qKey.isPressed)
                direction -= 1f;
            if (Keyboard.current.eKey.isPressed)
                direction += 1f;
            _yaw = Wrap(_yaw + direction * rotateDegreesPerSecond * Time.deltaTime);
        }

        void CreatePreview()
        {
            DestroyPreview();
            GameObject prefab = ResolvePrefab(_activeCatalogId, out string missing);
            if (prefab == null)
            {
                Report(missing);
                return;
            }

            _preview = Instantiate(prefab);
            _preview.name = "PlacementPreview";
            _preview.transform.localScale = Vector3.one * (_scale > 0f ? _scale : 1f);
            if (_preview.GetComponent<PlacementPreviewMarker>() == null)
                _preview.AddComponent<PlacementPreviewMarker>();
            var colliders = _preview.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
            var renderers = _preview.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
                Material[] materials = renderers[i].materials;
                for (int m = 0; m < materials.Length; m++)
                {
                    Tint(materials[m], new Color(0.45f, 1f, 0.55f, 0.5f));
                    _previewMaterials.Add(materials[m]);
                }

                renderers[i].materials = materials;
            }
        }

        void MovePreview(PlacementEvaluation evaluation)
        {
            if (_preview == null)
                return;
            _preview.SetActive(evaluation.HasPose);
            if (!evaluation.HasPose)
                return;
            _preview.transform.SetPositionAndRotation(evaluation.Position, Quaternion.Euler(0f, evaluation.Yaw, 0f));
            Color color = evaluation.Valid
                ? new Color(0.45f, 1f, 0.55f, 0.5f)
                : new Color(1f, 0.35f, 0.3f, 0.5f);
            for (int i = 0; i < _previewMaterials.Count; i++)
            {
                if (_previewMaterials[i] != null)
                    _previewMaterials[i].SetColor("_BaseColor", color);
            }
        }

        void ShowEvaluation(PlacementEvaluation evaluation)
        {
            if (_reasonText == null)
                return;
            _reasonText.text = evaluation.Reason;
            _reasonText.color = evaluation.Valid ? new Color(0.65f, 1f, 0.7f, 1f) : new Color(1f, 0.45f, 0.38f, 1f);
            if (_helpText != null)
            {
                _helpText.text = IsBird
                    ? "Point at the ground. Q and E rotate. Left click or Confirm places. Esc or Cancel stops."
                    : "The bin sits 2 m in front of you. Mouse aims. Q and E rotate. Left click or Confirm places. Esc or Cancel stops.";
            }
        }

        void DestroyPreview()
        {
            for (int i = 0; i < _previewMaterials.Count; i++)
            {
                if (_previewMaterials[i] != null)
                    Destroy(_previewMaterials[i]);
            }

            _previewMaterials.Clear();
            if (_preview != null)
                Destroy(_preview);
            _preview = null;
        }

        bool LoadDocument()
        {
            if (_loaded)
                return !_loadBlocked && _document != null;
            if (Store == null)
                Store = new JsonFilePlacementStore();
            if (!Store.TryLoad(CityId(), out PlacementDocument document, out string error))
            {
                _loadBlocked = true;
                _loaded = true;
                Report(string.IsNullOrEmpty(error) ? "Placements could not be loaded. Nothing was overwritten." : error);
                return false;
            }

            _document = document ?? new PlacementDocument();
            if (_document.placements == null)
                _document.placements = new List<PlacementRecord>();
            _loadBlocked = false;
            _loaded = true;
            if (!string.IsNullOrEmpty(error))
                Report(error);
            return true;
        }

        bool SaveDocument()
        {
            if (_loadBlocked)
            {
                Report("Placements were not saved because the existing file could not be read.");
                return false;
            }

            if (coordinates == null || !coordinates.originSaved)
            {
                Report("Assign " + PlaceableIds.CoordinatePath + " on CityWalker before saving a placement.");
                return false;
            }

            if (_document == null)
                _document = new PlacementDocument();
            _document.schemaVersion = PlacementDocument.CurrentSchema;
            _document.cityId = CityId();
            _document.originLongitude = coordinates.originLongitude;
            _document.originLatitude = coordinates.originLatitude;
            if (Store == null)
                Store = new JsonFilePlacementStore();
            if (!Store.TrySave(CityId(), _document, out string error))
            {
                Report("Could not save placements: " + error);
                return false;
            }

            Debug.Log("Saved placements to " + Store.Location(CityId()));
            return true;
        }

        GameObject ResolvePrefab(string catalogId, out string error)
        {
            error = null;
            PlaceableAssetEntry entry = catalog != null ? catalog.Find(catalogId) : null;
            if (entry != null && entry.prefab != null)
                return entry.prefab;
            if (catalogId == PlaceableIds.Bin && binPrefab != null)
                return binPrefab;
            error = catalogId == PlaceableIds.Bin
                ? PlaceableIds.MissingBin
                : "Prefab for '" + catalogId + "' is not assigned. No substitute object was spawned.";
            return null;
        }

        float ScaleOf(string catalogId)
        {
            PlaceableAssetEntry entry = catalog != null ? catalog.Find(catalogId) : null;
            if (entry != null && entry.uniformScale > 0f)
                return entry.uniformScale;
            return 1f;
        }

        string TypeOf(string catalogId)
        {
            PlaceableAssetEntry entry = catalog != null ? catalog.Find(catalogId) : null;
            if (entry != null && !string.IsNullOrEmpty(entry.assetType))
                return entry.assetType;
            return catalogId == PlaceableIds.Bin ? PlaceableIds.BinType : "asset";
        }

        bool TryGeographic(Vector3 world, out double longitude, out double latitude, out string error)
        {
            longitude = 0.0;
            latitude = 0.0;
            if (coordinates == null || !coordinates.originSaved)
            {
                error = "Assign " + PlaceableIds.CoordinatePath + " on CityWalker. A geographic position was not invented.";
                return false;
            }

            var projection = GeoProjection.FromOrigin(coordinates.originLongitude, coordinates.originLatitude);
            projection.Unproject(world.x, world.z, out longitude, out latitude);
            error = null;
            return true;
        }

        bool TryFlat(PlacementRecord record, out Vector3 flat, out string error)
        {
            flat = default;
            if (coordinates == null || !coordinates.originSaved)
            {
                error = "CityCoordinateFrame is not assigned, so the saved location cannot be projected.";
                return false;
            }

            var projection = GeoProjection.FromOrigin(coordinates.originLongitude, coordinates.originLatitude);
            flat = projection.Project(record.longitude, record.latitude);
            error = null;
            return true;
        }

        string CityId()
        {
            if (coordinates != null && !string.IsNullOrWhiteSpace(coordinates.cityId))
                return coordinates.cityId.Trim();
            return PlaceableIds.DefaultCityId;
        }

        bool CityReady()
        {
            if (catalog == null && binPrefab == null)
                return false;
            var boundary = FindAnyObjectByType<CityBoundaryMap>();
            if (boundary == null || boundary.loops == null || boundary.loops.Count == 0)
                return false;
            if (FindAnyObjectByType<CityTerrain>() == null && FindAnyObjectByType<TerrainChunk>() == null)
                return false;
            return coordinates != null && coordinates.originSaved;
        }

        void SyncWalkerFlags()
        {
            if (_walker == null)
                _walker = GetComponent<FirstPersonWalker>();
            if (_walker == null)
                return;
            bool placing = _state == SessionState.Placing;
            _walker.AllowLookWhileUnlocked = placing && !IsBird && !PointerOverUi();
            _walker.SuppressFlightVertical = _state != SessionState.Explore;
        }

        bool IsBird => ViewModeManager.Instance != null && ViewModeManager.Instance.Current == ExplorationMode.BirdsEye;

        bool GameplayOn()
        {
            var manager = ViewModeManager.Instance;
            if (manager == null)
                return true;
            return manager.GameplayAllowed;
        }

        Camera ActiveCamera()
        {
            var manager = ViewModeManager.Instance;
            if (manager == null)
                return Camera.main;
            if (manager.Current == ExplorationMode.BirdsEye && manager.birdsEyeCamera != null)
                return manager.birdsEyeCamera;
            if (manager.Current == ExplorationMode.ThirdPerson && manager.thirdPersonCamera != null)
                return manager.thirdPersonCamera;
            if (manager.firstPersonCamera != null)
                return manager.firstPersonCamera;
            return Camera.main;
        }

        bool ConfirmPressed()
        {
            bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            bool enter = Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
            return click || enter;
        }

        bool PointerOverUi()
        {
            if (EventSystem.current == null || Mouse.current == null)
                return false;
            var data = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
            _uiHits.Clear();
            EventSystem.current.RaycastAll(data, _uiHits);
            return _uiHits.Count > 0;
        }

        void Bind()
        {
            var hud = transform.Find("WalkHud");
            if (hud == null)
                return;
            _addButton = FindButton(hud, PlacementHudBuilder.AddButtonName);
            _catalogPanel = FindObject(hud, PlacementHudBuilder.CatalogName);
            _catalogList = FindDeep(hud, PlacementHudBuilder.CatalogListName);
            _catalogMessage = FindText(_catalogPanel != null ? _catalogPanel.transform : null, "Message");
            _instruction = FindObject(hud, PlacementHudBuilder.InstructionName);
            _reasonText = FindText(_instruction != null ? _instruction.transform : null, "Reason");
            _helpText = FindText(_instruction != null ? _instruction.transform : null, "Help");
            _manage = FindObject(hud, PlacementHudBuilder.ManageName);
            _manageTitle = FindText(_manage != null ? _manage.transform : null, "Title");
            var status = hud.Find(PlacementHudBuilder.LoadStatusName);
            _loadStatus = status != null ? status.GetComponent<Text>() : null;
            Listen(_addButton, OpenCatalog);
            Listen(FindButton(_catalogPanel != null ? _catalogPanel.transform : null, "CloseCatalogButton"), CloseCatalog);
            Listen(FindButton(_instruction != null ? _instruction.transform : null, "ConfirmButton"), Confirm);
            Listen(FindButton(_instruction != null ? _instruction.transform : null, "CancelButton"), CancelPlacement);
            Listen(FindButton(_manage != null ? _manage.transform : null, "MoveButton"), BeginMove);
            Listen(FindButton(_manage != null ? _manage.transform : null, "RotateButton"), BeginRotate);
            Listen(FindButton(_manage != null ? _manage.transform : null, "DeleteButton"), DeleteSelected);
            Listen(FindButton(_manage != null ? _manage.transform : null, "CloseManageButton"), ClearSelection);
        }

        void RefreshHud()
        {
            bool gameplay = GameplayOn();
            if (_addButton != null)
                _addButton.gameObject.SetActive(_state == SessionState.Explore && gameplay);
            if (_catalogPanel != null)
                _catalogPanel.SetActive(_state == SessionState.Catalog);
            if (_instruction != null)
                _instruction.SetActive(_state == SessionState.Placing);
            if (_manage != null)
                _manage.SetActive(_state == SessionState.Selected);
            if (_state == SessionState.Catalog && _catalogPanel != null)
                _catalogPanel.transform.SetAsLastSibling();
            if (_state == SessionState.Placing && _instruction != null)
                _instruction.transform.SetAsLastSibling();
        }

        Transform EnsureRoot()
        {
            var existing = FindAnyObjectByType<PlacedAssetsRoot>();
            if (existing != null)
                return existing.transform;
            var root = new GameObject("PlacedAssets");
            var city = GameObject.Find("Nazareth");
            if (city != null)
                root.transform.SetParent(city.transform, false);
            root.AddComponent<PlacedAssetsRoot>();
            return root.transform;
        }

        PlacementRecord FindRecord(string id)
        {
            if (_document == null || _document.placements == null || string.IsNullOrEmpty(id))
                return null;
            for (int i = 0; i < _document.placements.Count; i++)
            {
                if (_document.placements[i] != null && _document.placements[i].id == id)
                    return _document.placements[i];
            }

            return null;
        }

        PlacedAssetMarker FindMarker(string id)
        {
            var markers = FindObjectsByType<PlacedAssetMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                if (markers[i] != null && markers[i].placementId == id)
                    return markers[i];
            }

            return null;
        }

        static void CopyToMarker(PlacedAssetMarker marker, PlacementRecord record)
        {
            marker.placementId = record.id;
            marker.catalogId = record.catalogId;
            marker.assetType = record.assetType;
            marker.longitude = record.longitude;
            marker.latitude = record.latitude;
            marker.groundOffset = record.groundOffset;
            marker.rotationY = record.rotationY;
            marker.scale = record.scale;
            marker.createdUtc = record.createdUtc;
        }

        static void CopyFields(PlacementRecord from, PlacementRecord to)
        {
            to.longitude = from.longitude;
            to.latitude = from.latitude;
            to.groundOffset = from.groundOffset;
            to.rotationY = from.rotationY;
            to.scale = from.scale;
        }

        static void SetShown(GameObject go, bool shown)
        {
            if (go == null)
                return;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = shown;
            var colliders = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = shown;
        }

        static void Tint(Material material, Color color)
        {
            if (material == null)
                return;
            material.SetColor("_BaseColor", color);
            if (!material.HasProperty("_Surface"))
                return;
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 1);
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        void Report(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;
            _problems.Add(message);
            Debug.LogWarning(message);
            ShowProblems();
        }

        void ShowProblems()
        {
            if (_loadStatus == null || _problems.Count == 0)
                return;
            int start = Mathf.Max(0, _problems.Count - 3);
            var lines = new List<string>();
            for (int i = start; i < _problems.Count; i++)
                lines.Add(_problems[i]);
            _loadStatus.text = string.Join("\n", lines);
            _loadStatus.gameObject.SetActive(true);
            _statusUntil = Time.unscaledTime + 14f;
        }

        static void Discard(GameObject go)
        {
            if (go == null)
                return;
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        static float Wrap(float yaw)
        {
            yaw %= 360f;
            if (yaw < 0f)
                yaw += 360f;
            return yaw;
        }

        static string Short(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "item";
            return id.Length <= 8 ? id : id.Substring(0, 8);
        }

        static void Listen(Button button, UnityEngine.Events.UnityAction call)
        {
            if (button == null)
                return;
            button.onClick.RemoveListener(call);
            button.onClick.AddListener(call);
        }

        static Button FindButton(Transform root, string name)
        {
            Transform found = FindDeep(root, name);
            return found != null ? found.GetComponent<Button>() : null;
        }

        static Text FindText(Transform root, string name)
        {
            Transform found = FindDeep(root, name);
            return found != null ? found.GetComponent<Text>() : null;
        }

        static GameObject FindObject(Transform root, string name)
        {
            Transform found = root != null ? root.Find(name) : null;
            return found != null ? found.gameObject : null;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == name)
                    return all[i];
            }

            return null;
        }
    }
}
