using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Runtime.InteropServices;
using LogLevel = IPA.Logging.Logger.Level;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.SceneManagement;
using VRUIControls;
using Screen = UnityEngine.Screen;

namespace CameraPlus
{
    public class CameraPlusBehaviour : MonoBehaviour
    {
        public enum CursorType
        {
            None,
            Horizontal,
            Vertical,
            DiagonalLeft,
            DiagonalRight
        }

        protected readonly WaitUntil _waitForMainCamera = new WaitUntil(() => Camera.main);
        private readonly WaitForSecondsRealtime _waitForSecondsRealtime = new WaitForSecondsRealtime(1f);
        protected const int OnlyInThirdPerson = 3;
        protected const int UILayer = 5;
        protected const int OnlyInFirstPerson = 6; //Moved to an empty layer because layer 4 overlapped the floor
        protected const int NotesDebriLayer = 9;
        protected const int AlwaysVisible = 10;

        public bool ThirdPerson {
            get { return _thirdPerson; }
            set {
                _thirdPerson = value;
                _cameraCube.gameObject.SetActive(_thirdPerson && Config.showThirdPersonCamera);
                //_cameraPreviewQuad.gameObject.SetActive(false);

                if (value)
                {
                    _cam.cullingMask &= ~(1 << OnlyInFirstPerson);
                    _cam.cullingMask |= 1 << OnlyInThirdPerson;

                }
                else
                {
                    _cam.cullingMask &= ~(1 << OnlyInThirdPerson);
                    _cam.cullingMask |= 1 << OnlyInFirstPerson;
                }
            }
        }

        protected bool _thirdPerson;
        public Vector3 ThirdPersonPos;
        public Vector3 ThirdPersonRot;
        public Vector3 OffsetPosition;
        public Vector3 OffsetAngle;
        public Config Config;
        protected Material _previewMaterial;
        protected Camera _cam;
        protected Transform _cameraCube;
        protected GameObject _cameraPreviewQuad;
        protected Camera _mainCamera = null;
        protected CameraMoverPointer _moverPointer = null;
        protected GameObject _cameraCubeGO;
        protected GameObject _quad;
        protected CameraMovement _cameraMovement = null;
        protected BeatLineManager _beatLineManager;
        protected EnvironmentSpawnRotation _environmentSpawnRotation;

        protected int _prevScreenWidth;
        protected int _prevScreenHeight;
        protected int _prevAA;
        protected float _prevRenderScale;
        protected int _prevLayer;
        protected int _prevScreenPosX, _prevScreenPosY;
        protected bool _prevFitToCanvas;
        protected float _aspectRatio;
        protected float _yAngle;

        protected bool _wasWindowActive = false;
        protected bool _mouseHeld = false;
        protected bool _isResizing = false;
        protected bool _isMoving = false;
        protected bool _xAxisLocked = false;
        protected bool _yAxisLocked = false;
        protected bool _contextMenuOpen = false;
        internal bool _isCameraDestroyed = false;
        protected bool _isMainCamera = false;
        protected bool _isTopmostAtCursorPos = false;
        protected DateTime _lastRenderUpdate;
        protected Vector2 _initialOffset = new Vector2(0, 0);
        protected Vector2 _lastGrabPos = new Vector2(0, 0);
        protected Vector2 _lastScreenPos;
        protected bool _isBottom = false, _isLeft = false;
        protected static GameObject MenuObj = null;
        protected static ContextMenu _contextMenu = null;
        public static CursorType currentCursor = CursorType.None;
        public static bool wasWithinBorder = false;
        public static bool anyInstanceBusy = false;
        private static bool _contextMenuEnabled = true;

        public virtual void Init(Config config)
        {
            DontDestroyOnLoad(gameObject);
            Logger.Log("Created new camera plus behaviour component!");

            Config = config;
            _isMainCamera = Path.GetFileName(Config.FilePath) == $"{Plugin.MainCamera}.cfg";
            _contextMenuEnabled = Array.IndexOf(Environment.GetCommandLineArgs(), "fpfc") == -1;

            StartCoroutine(DelayedInit());
        }

        protected IEnumerator DelayedInit()
        {
            yield return _waitForMainCamera;

            _mainCamera = Camera.main;
            //      _menuStrip = null;
            if (_contextMenu == null)
            {
                MenuObj = new GameObject("CameraPlusMenu");
                _contextMenu = MenuObj.AddComponent<ContextMenu>();
            }
            XRSettings.showDeviceView = false;


            var gameObj = Instantiate(_mainCamera.gameObject);

            Config.ConfigChangedEvent += PluginOnConfigChangedEvent;

            gameObj.SetActive(false);
            gameObj.name = "Camera Plus";
            gameObj.tag = "Untagged";
            while (gameObj.transform.childCount > 0) DestroyImmediate(gameObj.transform.GetChild(0).gameObject);
            //DestroyImmediate(gameObj.GetComponent(typeof(CameraRenderCallbacksManager)));
            DestroyImmediate(gameObj.GetComponent("AudioListener"));
            DestroyImmediate(gameObj.GetComponent("MeshCollider"));

            _cam = gameObj.GetComponent<Camera>();
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.enabled = true;
            _cam.name = Path.GetFileName(Config.FilePath);

            var _liv = _cam.GetComponent<LIV.SDK.Unity.LIV>();
            if (_liv)
                Destroy(_liv);

            if (_previewMaterial == null)
                _previewMaterial = new Material(Shader.Find("Hidden/BlitCopyWithDepth"));

            gameObj.SetActive(true);

            var camera = _mainCamera.transform;
            transform.position = camera.position;
            transform.rotation = camera.rotation;
            Logger.Log($"near clipplane \"{Camera.main.nearClipPlane}");

            gameObj.transform.parent = transform;
            gameObj.transform.localPosition = Vector3.zero;
            gameObj.transform.localRotation = Quaternion.identity;
            gameObj.transform.localScale = Vector3.one;

            _cameraCubeGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DontDestroyOnLoad(_cameraCubeGO);
            _cameraCubeGO.SetActive(ThirdPerson);
            _cameraCube = _cameraCubeGO.transform;
            _cameraCube.localScale = new Vector3(0.15f, 0.15f, 0.22f);
            _cameraCube.name = "CameraCube";
            _cameraCubeGO.layer = Plugin.Instance._rootConfig.CameraQuadLayer;

            /*
            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            DontDestroyOnLoad(_quad);
            DestroyImmediate(_quad.GetComponent<Collider>());
            _quad.GetComponent<MeshRenderer>().material = _previewMaterial;
            _quad.transform.parent = _cameraCube;
            _quad.transform.localPosition = new Vector3(-1f * ((_cam.aspect - 1) / 2 + 1), 0, 0.22f);
            _quad.transform.localEulerAngles = new Vector3(0, 180, 0);
            _quad.transform.localScale = new Vector3(_cam.aspect, 1, 1);
            _cameraPreviewQuad = _quad;
            */
            ReadConfig();

            if (ThirdPerson)
            {
                ThirdPersonPos = Config.Position;
                ThirdPersonRot = Config.Rotation;

                transform.position = ThirdPersonPos;
                transform.eulerAngles = ThirdPersonRot;

                _cameraCube.position = ThirdPersonPos;
                _cameraCube.eulerAngles = ThirdPersonRot;
            }

            // Add our camera movement script if the movement script path is set
            if (Config.movementScriptPath != String.Empty)
                AddMovementScript();
            SetCullingMask();
            CameraMovement.CreateExampleScript();
            Plugin.Instance.ActiveSceneChanged += SceneManager_activeSceneChanged;
            //      FirstPersonOffset = Config.FirstPersonPositionOffset;
            //       FirstPersonRotationOffset = Config.FirstPersonRotationOffset;
            SceneManager_activeSceneChanged(new Scene(), new Scene());
            Logger.Log($"Camera \"{Path.GetFileName(Config.FilePath)}\" successfully initialized! {Convert.ToString(_cam.cullingMask,16)}");

            if (!Plugin.Instance.MultiplayerSessionInit)
            {
                Plugin.Instance.MultiplayerSessionInit = true;
                MultiplayerSession.Init();
            }
        }

        protected virtual void OnDestroy()
        {
            Config.ConfigChangedEvent -= PluginOnConfigChangedEvent;
            Plugin.Instance.ActiveSceneChanged -= SceneManager_activeSceneChanged;

            _cameraMovement?.Shutdown();

            // Close our context menu if its open, and destroy all associated controls, otherwise the game will lock up
            CloseContextMenu();

            if (_cameraCubeGO)
                Destroy(_cameraCubeGO);
            if (_quad)
                Destroy(_quad);
        }

        protected virtual void PluginOnConfigChangedEvent(Config config)
        {
            ReadConfig();
        }

        protected virtual void ReadConfig()
        {
            ThirdPerson = Config.thirdPerson;

            if (!ThirdPerson)
            {
                transform.position = _mainCamera.transform.position;
                transform.rotation = _mainCamera.transform.rotation;
            }
            else
            {
                ThirdPersonPos = Config.Position;
                ThirdPersonRot = Config.Rotation;
                //      FirstPersonOffset = Config.FirstPersonPositionOffset;
                //      FirstPersonRotationOffset = Config.FirstPersonRotationOffset;
            }

            SetCullingMask();
            CreateScreenRenderTexture();
            SetFOV();
        }

        internal virtual void CreateScreenRenderTexture()
        {
            HMMainThreadDispatcher.instance.Enqueue(delegate
            {
                if (Config.fitToCanvas)
                {
                    Config.screenPosX = 0;
                    Config.screenPosY = 0;
                    Config.screenWidth = Screen.width;
                    Config.screenHeight = Screen.height;
                }                

                _lastRenderUpdate = DateTime.Now;
                //GetScaledScreenResolution(Config.renderScale, out var scaledWidth, out var scaledHeight);
                
                _cam.pixelRect = new Rect(Config.ScreenPosition, Config.ScreenSize);
                _cam.clearFlags = CameraClearFlags.Depth;
                _cam.depth = Config.layer;

                _prevFitToCanvas = Config.fitToCanvas;
                _prevAA = Config.antiAliasing;
                _prevRenderScale = Config.renderScale;
                _prevScreenHeight = Config.screenHeight;
                _prevScreenWidth = Config.screenWidth;
                _prevLayer = Config.layer;
                _prevScreenPosX = Config.screenPosX;
                _prevScreenPosY = Config.screenPosY;
            });
        }

        public virtual void SceneManager_activeSceneChanged(Scene from, Scene to)
        {
            StartCoroutine(GetMainCamera());
            StartCoroutine(Get360Managers());
            var vrPointers = to.name == "GameCore" ? Resources.FindObjectsOfTypeAll<VRPointer>() : Resources.FindObjectsOfTypeAll<VRPointer>();
            if (vrPointers.Count() == 0)
            {
                Logger.Log("Failed to get VRPointer!", LogLevel.Warning);
                return;
            }

            var pointer = to.name != "GameCore" ? vrPointers.First() : vrPointers.Last();
            if (_moverPointer) Destroy(_moverPointer);
            _moverPointer = pointer.gameObject.AddComponent<CameraMoverPointer>();
            _moverPointer.Init(this, _cameraCube);

            if (to.name == "GameCore" && Config.movementScriptPath != String.Empty && Config.movementAudioSync)
            {
                AddMovementScript();
                Logger.Log($"Add MoveScript \"{Path.GetFileName(Config.movementScriptPath)}\" successfully initialized! {Convert.ToString(_cam.cullingMask, 16)}");
            }
        }

        protected virtual void Update()
        {
            // Only toggle the main camera in/out of third person with f1, not any extra cams
            if (_isMainCamera)
            {
                if (Input.GetKeyDown(KeyCode.F1))
                {
                    ThirdPerson = !ThirdPerson;
                    if (!ThirdPerson)
                    {
                        transform.position = _mainCamera.transform.position;
                        transform.rotation = _mainCamera.transform.rotation;
                        //          FirstPersonOffset = Config.FirstPersonPositionOffset;
                        //            FirstPersonRotationOffset = Config.FirstPersonRotationOffset;
                    }
                    else
                    {
                        ThirdPersonPos = Config.Position;
                        ThirdPersonRot = Config.Rotation;
                    }

                    Config.thirdPerson = ThirdPerson;
                    Config.Save();
                }
            }
            HandleMouseEvents();
            //PlayerOffset = multiplayerConnectedPlayerSpectatingSpot.transform.position;
            //Logger.Log($"Player Offset : {PlayerOffset.x},{PlayerOffset.y},{PlayerOffset.z}", LogLevel.Notice);
        }

        protected virtual void LateUpdate()
        {
            try
            {
                OffsetPosition = new Vector3();
                OffsetAngle = new Vector3();

                var camera = _mainCamera.transform;

                HandleMultiPlayerLobby();
                HandleMultiPlayerGame();

                if (ThirdPerson)
                {
                    HandleThirdPerson360();

                    transform.position = ThirdPersonPos;
                    transform.eulerAngles = ThirdPersonRot;
                    _cameraCube.position = ThirdPersonPos;
                    _cameraCube.eulerAngles = ThirdPersonRot;

                    if (OffsetPosition != Vector3.zero && OffsetAngle != Vector3.zero)
                    {
                        transform.position = ThirdPersonPos + OffsetPosition;
                        transform.eulerAngles = ThirdPersonRot + OffsetAngle;
                        _cameraCube.position = ThirdPersonPos + OffsetPosition;
                        _cameraCube.eulerAngles = ThirdPersonRot + OffsetAngle;

                        Quaternion angle = Quaternion.AngleAxis(OffsetAngle.y, Vector3.up);
                        transform.position -= OffsetPosition;
                        transform.position = angle * transform.position;
                        transform.position += OffsetPosition;
                        _cameraCube.position = transform.position;
                    }

                    return;
                }
                //     Console.WriteLine(Config.FirstPersonPositionOffset.ToString());
                transform.position = Vector3.Lerp(transform.position, camera.position + Config.FirstPersonPositionOffset,
                    Config.positionSmooth * Time.unscaledDeltaTime);

                if (!Config.forceFirstPersonUpRight)
                    transform.rotation = Quaternion.Slerp(transform.rotation, camera.rotation * Quaternion.Euler(Config.FirstPersonRotationOffset),
                        Config.rotationSmooth * Time.unscaledDeltaTime);
                else

                {
                    Quaternion rot = Quaternion.Slerp(transform.rotation, camera.rotation * Quaternion.Euler(Config.FirstPersonRotationOffset),
                        Config.rotationSmooth * Time.unscaledDeltaTime);
                    transform.rotation = rot * Quaternion.Euler(0, 0, -(rot.eulerAngles.z));
                }
            }
            catch{ }
        }

        private void HandleThirdPerson360()
        {
            if (!_beatLineManager || !Config.use360Camera || !_environmentSpawnRotation) return;
            
            float b;
            if (_beatLineManager.isMidRotationValid)
            {
                double midRotation = (double)this._beatLineManager.midRotation;
                float num1 = Mathf.DeltaAngle((float)midRotation, this._environmentSpawnRotation.targetRotation);
                float num2 = (float)(-(double)this._beatLineManager.rotationRange * 0.5);
                float num3 = this._beatLineManager.rotationRange * 0.5f;
                if ((double)num1 > (double)num3)
                    num3 = num1;
                else if ((double)num1 < (double)num2)
                    num2 = num1;
                b = (float)(midRotation + ((double)num2 + (double)num3) * 0.5);
            }
            else
                b = this._environmentSpawnRotation.targetRotation;

            if (Config.cam360RotateControlNew)
                _yAngle = Mathf.LerpAngle(_yAngle, b, Mathf.Clamp(Time.deltaTime * Config.cam360Smoothness, 0f, 1f));
            else
                _yAngle = Mathf.Lerp(_yAngle, b, Mathf.Clamp(Time.deltaTime * Config.cam360Smoothness, 0f, 1f));

            ThirdPersonRot = new Vector3(Config.cam360XTilt, _yAngle + Config.cam360YTilt, Config.cam360ZTilt);

            ThirdPersonPos = (transform.forward * Config.cam360ForwardOffset) + (transform.right * Config.cam360RightOffset);
            ThirdPersonPos = new Vector3(ThirdPersonPos.x, Config.cam360UpOffset, ThirdPersonPos.z);
        }

        private void HandleMultiPlayerLobby()
        {
            try
            {
                if (MultiplayerSession.LobbyContoroller == null || !MultiplayerSession.LobbyContoroller.isActiveAndEnabled || Config.MultiPlayerNumber == 0) return;
                if (MultiplayerSession.LobbyAvatarPlace.Count == 0) MultiplayerSession.LoadLobbyAvatarPlace();

                for (int i=0; i< MultiplayerSession.LobbyAvatarPlace.Count;i++)
                {
                    if (i==Config.MultiPlayerNumber - 1)
                    {
                        OffsetPosition = MultiplayerSession.LobbyAvatarPlace[i].position;
                        OffsetAngle = MultiplayerSession.LobbyAvatarPlace[i].eulerAngles;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"HandleMultiPlayerLobby Error {ex.Message}", LogLevel.Error);
            }
        }
        private void HandleMultiPlayerGame()
        {
            try
            {
                if (SceneManager.GetActiveScene().name == "GameCore" && MultiplayerSession.ConnectedMultiplay)
                {
                    MultiplayerConnectedPlayerFacade player = null;
                    bool TryPlayerFacade;
                    if (MultiplayerSession.playersManager == null)
                    {
                        MultiplayerSession.playersManager = Resources.FindObjectsOfTypeAll<MultiplayerPlayersManager>().FirstOrDefault();
                        Logger.Log($"{this.name} Set MultiplayerPlayersManager", LogLevel.Notice);
                    }
                    if (Config.MultiPlayerNumber != 0 && MultiplayerSession.playersManager != null)
                    {
                        foreach(IConnectedPlayer connectedPlayer in MultiplayerSession.connectedPlayers)
                        {
                            if (Config.MultiPlayerNumber - 1 == connectedPlayer.sortIndex)
                            {
                                TryPlayerFacade = MultiplayerSession.playersManager.TryGetConnectedPlayerController(connectedPlayer.userId, out player);
                                if (TryPlayerFacade && player != null)
                                {
                                    OffsetPosition = player.transform.position;
                                    OffsetAngle = player.transform.eulerAngles;
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"{this.name} HandleMultiPlayerGame Error {ex.Message}", LogLevel.Error);
            }
        }
        public void AddMovementScript()
        {
            if (Config.movementScriptPath != String.Empty)
            {
                if (_cameraMovement)
                    _cameraMovement.Shutdown();

                if (Config.movementScriptPath == "SongMovementScript")
                    _cameraMovement = _cam.gameObject.AddComponent<SongCameraMovement>();
                else if (File.Exists(Config.movementScriptPath) || 
                        File.Exists(Path.Combine(UnityGame.UserDataPath, Plugin.Name, "Scripts", Config.movementScriptPath)) || 
                        File.Exists(Path.Combine(UnityGame.UserDataPath, Plugin.Name, "Scripts", Path.GetFileName(Config.movementScriptPath))))
                    _cameraMovement = _cam.gameObject.AddComponent<CameraMovement>();
                else
                    return;
                if (_cameraMovement.Init(this))
                {
                    ThirdPersonPos = Config.Position;
                    ThirdPersonRot = Config.Rotation;
                    Config.thirdPerson = true;
                    ThirdPerson = true;
                    CreateScreenRenderTexture();
                }
            }
        }
        public void ClearMovementScript()
        {
            if (Config.movementScriptPath == String.Empty)
            {
                if (_cameraMovement)
                    _cameraMovement.Shutdown();
                if (_cameraMovement.Init(this))
                {
                    ThirdPersonPos = Config.Position;
                    ThirdPersonRot = Config.Rotation;
                    Config.thirdPerson = true;
                    ThirdPerson = true;
                    CreateScreenRenderTexture();
                }
            }
        }

        protected IEnumerator GetMainCamera()
        {
            yield return _waitForMainCamera;
            _mainCamera = Camera.main;
        }

        protected IEnumerator Get360Managers() {
            yield return new WaitForSeconds(0.5f);

            _beatLineManager = null;
            _environmentSpawnRotation = null;

            var testList = Resources.FindObjectsOfTypeAll<BeatLineManager>();

            if (testList.Length > 0)
            {
                _beatLineManager = testList.FirstOrDefault();

                _environmentSpawnRotation = Resources.FindObjectsOfTypeAll<EnvironmentSpawnRotation>().FirstOrDefault();
            }

            if (_beatLineManager)
            {
                this._yAngle = _beatLineManager.midRotation;
            }

        }

        internal virtual void SetFOV()
        {
            if (_cam == null) return;
            _cam.fieldOfView = Config.fov;
        }

        internal virtual void FOV(float FOV)
        {
            _cam.fieldOfView = FOV;
        }

        internal virtual void SetCullingMask()
        {
            _cam.cullingMask = Camera.main.cullingMask;
            if (Config.transparentWalls)
                _cam.cullingMask &= ~(1 << TransparentWallsPatch.WallLayerMask);
            else
                _cam.cullingMask |= (1 << TransparentWallsPatch.WallLayerMask);
            if (Config.avatar)
            {
                if (Config.avatarOnly)
                {
                    _cam.cullingMask = 0; //Everything is culled.
                }
                if (Config.thirdPerson || Config.use360Camera)
                {
                    _cam.cullingMask |= 1 << OnlyInThirdPerson;
                    _cam.cullingMask &= ~(1 << OnlyInFirstPerson); 
                }
                else
                {
                    _cam.cullingMask |= 1 << OnlyInFirstPerson;
                    _cam.cullingMask &= ~(1 << OnlyInThirdPerson);
                }
                _cam.cullingMask |= 1 << AlwaysVisible;
             }
            else
            {
                _cam.cullingMask &= ~(1 << OnlyInThirdPerson);
                _cam.cullingMask &= ~(1 << OnlyInFirstPerson);
                _cam.cullingMask &= ~(1 << AlwaysVisible);
            }
            if (Config.debri!="link")
            {
                if (Config.debri=="show")
                    _cam.cullingMask |= (1 << NotesDebriLayer);
                else
                    _cam.cullingMask &= ~(1 << NotesDebriLayer);
            }
            if (Config.displayUI)
                _cam.cullingMask &= ~(1 << UILayer);
            else
                _cam.cullingMask |= (1 << UILayer);
        }

        public bool IsWithinRenderArea(Vector2 mousePos, Config c)
        {
            if (mousePos.x < c.screenPosX) return false;
            if (mousePos.x > c.screenPosX + c.screenWidth) return false;
            if (mousePos.y < c.screenPosY) return false;
            if (mousePos.y > c.screenPosY + c.screenHeight) return false;
            return true;
        }

        public bool IsTopmostRenderAreaAtPos(Vector2 mousePos)
        {
            if (!IsWithinRenderArea(mousePos, Config)) return false;
            foreach (CameraPlusInstance c in Plugin.Instance.Cameras.Values.ToArray())
            {
                if (c.Instance == this) continue;
                if (!IsWithinRenderArea(mousePos, c.Config) && !c.Instance._mouseHeld) continue;
                if (c.Config.layer > Config.layer)
                {
                    return false;
                }

                if (c.Config.layer == Config.layer &&
                    c.Instance._lastRenderUpdate > _lastRenderUpdate)
                {
                    return false;
                }

                if (c.Instance._mouseHeld && (c.Instance._isMoving ||
                    c.Instance._isResizing || c.Instance._contextMenuOpen))
                {
                    return false;
                }
            }
            return true;
        }

        public static CameraPlusBehaviour GetTopmostInstanceAtCursorPos()
        {
            foreach (CameraPlusInstance c in Plugin.Instance.Cameras.Values.ToArray())
            {
                if (c.Instance._isTopmostAtCursorPos)
                    return c.Instance;
            }
            return null;
        }

        internal void CloseContextMenu()
        {
            _contextMenu.DisableMenu();
            Destroy(MenuObj);
            _contextMenuOpen = false;
        }

        public static void SetCursor(CursorType type)
        {
            if (type != currentCursor)
            {
                Texture2D texture = null;
                switch (type)
                {
                    case CursorType.Horizontal:
                        texture = Utils.LoadTextureFromResources("CameraPlus.Resources.Resize_Horiz.png");
                        break;
                    case CursorType.Vertical:
                        texture = Utils.LoadTextureFromResources("CameraPlus.Resources.Resize_Vert.png");
                        break;
                    case CursorType.DiagonalRight:
                        texture = Utils.LoadTextureFromResources("CameraPlus.Resources.Resize_DiagRight.png");
                        break;
                    case CursorType.DiagonalLeft:
                        texture = Utils.LoadTextureFromResources("CameraPlus.Resources.Resize_DiagLeft.png");
                        break;
                }
                UnityEngine.Cursor.SetCursor(texture, texture ? new Vector2(texture.width / 2, texture.height / 2) : new Vector2(0, 0), CursorMode.Auto);
                currentCursor = type;
            }
        }

        protected void HandleMouseEvents()
        {
            bool holdingLeftClick = Input.GetMouseButton(0);
            bool holdingRightClick = Input.GetMouseButton(1);

            Vector3 mousePos = Input.mousePosition;

            // Close the context menu when we click anywhere within the bounds of the application
            if (!_mouseHeld && (holdingLeftClick || holdingRightClick))
            {
                if (/*_menuStrip != null &&*/ mousePos.x > 0 && mousePos.x < Screen.width && mousePos.y > 0 && mousePos.y < Screen.height)
                {
                    //          CloseContextMenu();
                }
            }

            _isTopmostAtCursorPos = IsTopmostRenderAreaAtPos(mousePos);
            // Only evaluate mouse events for the topmost render target at the mouse position
            if (!_mouseHeld && !_isTopmostAtCursorPos) return;

            int tolerance = 5;
            bool cursorWithinBorder = Utils.WithinRange((int)mousePos.x, -tolerance, tolerance) || Utils.WithinRange((int)mousePos.y, -tolerance, tolerance) ||
                Utils.WithinRange((int)mousePos.x, Config.screenPosX + Config.screenWidth - tolerance, Config.screenPosX + Config.screenWidth + tolerance) ||
                Utils.WithinRange((int)mousePos.x, Config.screenPosX - tolerance, Config.screenPosX + tolerance) ||
                Utils.WithinRange((int)mousePos.y, Config.screenPosY + Config.screenHeight - tolerance, Config.screenPosY + Config.screenHeight + tolerance) ||
                Utils.WithinRange((int)mousePos.y, Config.screenPosY - tolerance, Config.screenPosY + tolerance);

            float currentMouseOffsetX = mousePos.x - Config.screenPosX;
            float currentMouseOffsetY = mousePos.y - Config.screenPosY;
            if (!_mouseHeld)
            {
                if (cursorWithinBorder)
                {
                    var isLeft = currentMouseOffsetX <= Config.screenWidth / 2;
                    var isBottom = currentMouseOffsetY <= Config.screenHeight / 2;
                    var centerX = Config.screenPosX + (Config.screenWidth / 2);
                    var centerY = Config.screenPosY + (Config.screenHeight / 2);
                    var offsetX = Config.screenWidth / 2 - tolerance;
                    var offsetY = Config.screenHeight / 2 - tolerance;
                    _xAxisLocked = Utils.WithinRange((int)mousePos.x, centerX - offsetX + 1, centerX + offsetX - 1);
                    _yAxisLocked = Utils.WithinRange((int)mousePos.y, centerY - offsetY + 1, centerY + offsetY - 1);

                    if (!Config.fitToCanvas)
                    {
                        if (_xAxisLocked)
                            SetCursor(CursorType.Vertical);
                        else if (_yAxisLocked)
                            SetCursor(CursorType.Horizontal);
                        else if (isLeft && isBottom || !isLeft && !isBottom)
                            SetCursor(CursorType.DiagonalLeft);
                        else if (isLeft && !isBottom || !isLeft && isBottom)
                            SetCursor(CursorType.DiagonalRight);
                    }
                    wasWithinBorder = true;
                }
                else if (!cursorWithinBorder && wasWithinBorder)
                {
                    SetCursor(CursorType.None);
                    wasWithinBorder = false;
                }
            }

            if (holdingLeftClick && !Config.fitToCanvas)
            {
                if (!_mouseHeld)
                {
                    _initialOffset.x = currentMouseOffsetX;
                    _initialOffset.y = currentMouseOffsetY;

                    _lastScreenPos = Config.ScreenPosition;
                    _lastGrabPos = new Vector2(mousePos.x, mousePos.y);

                    _isLeft = _initialOffset.x <= Config.screenWidth / 2;
                    _isBottom = _initialOffset.y <= Config.screenHeight / 2;
                    anyInstanceBusy = true;
                }
                _mouseHeld = true;

                if (!_isMoving && (_isResizing || cursorWithinBorder))
                {
                    _isResizing = true;
                    if (!_xAxisLocked)
                    {
                        int changeX = _isLeft ? (int)(_lastGrabPos.x - mousePos.x) : (int)(mousePos.x - _lastGrabPos.x);
                        Config.screenWidth += changeX;
                        Config.screenPosX = ((int)_lastScreenPos.x - (_isLeft ? changeX : 0));
                    }
                    if (!_yAxisLocked)
                    {
                        int changeY = _isBottom ? (int)(mousePos.y - _lastGrabPos.y) : (int)(_lastGrabPos.y - mousePos.y);
                        Config.screenHeight -= changeY;
                        Config.screenPosY = ((int)_lastScreenPos.y + (_isBottom ? changeY : 0));
                    }
                    _lastGrabPos = mousePos;
                    _lastScreenPos = Config.ScreenPosition;
                }
                else
                {
                    _isMoving = true;
                    Config.screenPosX = (int)mousePos.x - (int)_initialOffset.x;
                    Config.screenPosY = (int)mousePos.y - (int)_initialOffset.y;
                }
                Config.screenWidth = Mathf.Clamp(Config.screenWidth, 100, Screen.width);
                Config.screenHeight = Mathf.Clamp(Config.screenHeight, 100, Screen.height);
                Config.screenPosX = Mathf.Clamp(Config.screenPosX, 0, Screen.width - Config.screenWidth);
                Config.screenPosY = Mathf.Clamp(Config.screenPosY, 0, Screen.height - Config.screenHeight);

                CreateScreenRenderTexture();
            }
            else if (holdingRightClick && _contextMenuEnabled)
            {
                if (_mouseHeld) return;
                DisplayContextMenu();
                _contextMenuOpen = true;
                anyInstanceBusy = true;
                _mouseHeld = true;
            }
            else if (_isResizing || _isMoving || _mouseHeld)
            {
                if (!_contextMenuOpen)
                {
                    if (!_isCameraDestroyed)
                    {
                        Config.Save();
                    }
                }
                _isResizing = false;
                _isMoving = false;
                _mouseHeld = false;
                anyInstanceBusy = false;
            }
        }

        void DisplayContextMenu()
        {
            if (_contextMenu == null)
            {
                MenuObj = new GameObject("CameraPlusMenu");
                _contextMenu = MenuObj.AddComponent<ContextMenu>();
            }
            _contextMenu.EnableMenu(Input.mousePosition, this);
        }
    }
}
