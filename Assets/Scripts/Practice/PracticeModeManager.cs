using System.Collections;
using System.Collections.Generic;
using FishNet;
using FishNet.Connection;
using FishNet.Object;
using ProjectVoid.Network; // NetworkMapManager namespace
using ProjectVoid.Map; // Added for MapGenerationSettings
using UnityEngine;

namespace ProjectVoid.Practice
{
    public class PracticeModeManager : NetworkBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject _networkMapManagerPrefab;
        [SerializeField] private GameObject _gameStateManagerPrefab;
        [SerializeField] private GameObject _playerPrefab;

        [Header("Map Settings")]
        [SerializeField] private MapGenerationSettings _mapSettings;

        private NetworkMapManager _spawnedMapManager;
        private GameStateManager _spawnedGameStateManager;
        private bool _isInitializing = false;

        public override void OnStartServer()
        {
            base.OnStartServer();
            
            // Only execute on Host (Server)
            if (!IsServerInitialized) return;

            Debug.Log("[PracticeModeManager] OnStartServer called. Starting initialization...");
            StartCoroutine(InitializePracticeMode());


        }

        private IEnumerator InitializePracticeMode()
        {
            if (_isInitializing) yield break;
            _isInitializing = true;

            // 0. Show Loading Panel
            // Why: 맵 생성에 시간이 걸리므로 로딩 화면을 표시하여 UX 개선. 연습 모드는 대기 패널 생략(true)
            LoadingUIManager.Instance?.ShowLoadingAndWaitForMap(true);

            // 1. Spawn Managers
            yield return SpawnManagers();

            // 2. Initialize Game State
            InitializeGameState();

            // 3. Generate Map
            InitializeMap();

            // 4. Wait for Map Ready
            yield return WaitForMapReady();

            // 5. Spawn Player (Host Client)
            SpawnPlayer();
            
            // 6. Register Host Client as Scene Observer
            // Host 모드(Yak 포함)에서는 Host Client가 명시적으로 Observer로 등록되어야 
            // 렌더러 가시성 및 RPC(이펙트, 애니메이션) 수신이 정상 작동합니다.
            StartCoroutine(RegisterHostClientAsSceneObserverRoutine());

            _isInitializing = false;
        }
        
        /// <summary>
        /// Host 모드에서 clientHost를 모든 로드된 Scene의 Observer로 등록합니다.
        /// 이렇게 하면 모든 ObserversRpc(이펙트, 애니메이션)가 Host의 클라이언트에게 정상적으로 전달됩니다.
        /// </summary>
        private IEnumerator RegisterHostClientAsSceneObserverRoutine()
        {
            if (!IsServerInitialized) yield break;

            // Wait until a local client connection is available
            NetworkConnection localClientConn = null;
            float timeout = 5f;
            float elapsed = 0f;

            while (localClientConn == null && elapsed < timeout)
            {
                foreach (var conn in InstanceFinder.ServerManager.Clients.Values)
                {
                    if (conn.IsLocalClient)
                    {
                        localClientConn = conn;
                        break;
                    }
                }
                
                if (localClientConn == null)
                {
                    yield return null;
                    elapsed += Time.deltaTime;
                }
            }
            
            if (localClientConn == null)
            {
                Debug.LogWarning("[PracticeModeManager] Failed to find local client connection for observer registration (Timeout).");
                yield break;
            }
            
            // Register as observer for ALL loaded scenes
            // This ensures we catch objects in map scene, persistent scene, etc.
            int sceneCount = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    InstanceFinder.SceneManager.AddConnectionToScene(localClientConn, scene);
                    Debug.Log($"[PracticeModeManager] Registered host client (ID: {localClientConn.ClientId}) as observer for scene: {scene.name}");
                }
            }
        }

        private IEnumerator SpawnManagers()
        {
            // Spawn GameStateManager if not exists
            if (GameStateManager.Instance != null)
            {
                _spawnedGameStateManager = GameStateManager.Instance;
                Debug.Log("[PracticeModeManager] Using existing GameStateManager.");
            }
            else if (_gameStateManagerPrefab != null)
            {
                var go = Instantiate(_gameStateManagerPrefab);
                InstanceFinder.ServerManager.Spawn(go);
                _spawnedGameStateManager = go.GetComponent<GameStateManager>();
                Debug.Log("[PracticeModeManager] GameStateManager spawned.");
            }
            else
            {
                Debug.LogError("[PracticeModeManager] GameStateManagerPrefab is missing and no instance found!");
            }

            yield return null;

            // Spawn NetworkMapManager
            // In Practice Mode, NetworkManager should NOT have spawned this (as we are not in GamePlay scene or it's disabled)
            if (_networkMapManagerPrefab != null)
            {
                var go = Instantiate(_networkMapManagerPrefab);
                InstanceFinder.ServerManager.Spawn(go);
                _spawnedMapManager = go.GetComponent<NetworkMapManager>();
                
                Debug.Log("[PracticeModeManager] NetworkMapManager spawned.");
            }
            else
            {
                Debug.LogError("[PracticeModeManager] NetworkMapManagerPrefab is missing!");
            }
            
            yield return null;
        }

        private void InitializeGameState()
        {
            if (_spawnedGameStateManager == null) return;

            // Set Practice Mode (GameMode.PracticeRange, TargetPlayerCount = 1)
            // Use RPC or direct SyncVar modification if allowed (it is server so SyncVar direct set is best if public, but setter is internal usually)
            // Using RPC method provided in GameStateManager
            _spawnedGameStateManager.RPC_SetGameModeInfo((int)GameMode.PracticeRange, 1);
            
            Debug.Log("[PracticeModeManager] Game State initialized for Practice Mode.");
        }

        private void InitializeMap()
        {
            if (_spawnedMapManager == null) return;

            // Initialize Map for Practice Mode
            _spawnedMapManager.InitializeMap(GameMode.PracticeRange, 1);
            Debug.Log("[PracticeModeManager] Map initialization started.");
        }

        private IEnumerator WaitForMapReady()
        {
            if (_spawnedMapManager == null) yield break;

            Debug.Log("[PracticeModeManager] Waiting for map generation...");
            
            float timeout = 10f;
            float elapsed = 0f;

            while (!_spawnedMapManager.IsReady() && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (_spawnedMapManager.IsReady())
            {
                Debug.Log("[PracticeModeManager] Map is ready!");
            }
            else
            {
                Debug.LogError("[PracticeModeManager] Map generation timed out!");
            }
        }

        private void SpawnPlayer()
        {
            if (_playerPrefab == null)
            {
                Debug.LogError("[PracticeModeManager] PlayerPrefab is missing!");
                return;
            }

            if (_spawnedMapManager == null || !_spawnedMapManager.IsReady())
            {
                Debug.LogError("[PracticeModeManager] Cannot spawn player - Map not ready.");
                return;
            }

            // Get Spawn Position
            Vector3 spawnPos = _spawnedMapManager.GetPlayerSpawnPosition(0);
            Quaternion spawnRot = Quaternion.Euler(0, _spawnedMapManager.GetPlayerSpawnRotation(0), 0);

            // Spawn Player for the local connection (Owner)
            // Since this is Host mode, the local connection is ServerManager.Clients[?]
            NetworkConnection targetConn = null;
            Debug.Log($"[PracticeModeManager] Searching for Host Connection. Total Clients: {InstanceFinder.ServerManager.Clients.Count}");
            
            if (InstanceFinder.ServerManager.Clients.Count > 0)
            {
                foreach (var conn in InstanceFinder.ServerManager.Clients.Values)
                {
                    Debug.Log($"[PracticeModeManager] Checking Connection: Id={conn.ClientId}, IsLocalClient={conn.IsLocalClient}, IsActive={conn.IsActive}");
                    if (conn.IsLocalClient)
                    {
                        targetConn = conn;
                        break;
                    }
                }
            }

            if (targetConn == null && InstanceFinder.ServerManager.Clients.Count == 1)
            {
                var firstConn = InstanceFinder.ServerManager.Clients.Values.GetEnumerator();
                firstConn.MoveNext();
                targetConn = firstConn.Current;
                Debug.LogWarning($"[PracticeModeManager] IsLocalClient match failed. Using the only available connection: {targetConn.ClientId}");
            }
            
            if (targetConn != null)
            {
                GameObject playerGO = Instantiate(_playerPrefab, spawnPos, spawnRot);
                
                // [DEBUG] Check Player Rendering State BEFORE Spawn
                Debug.Log($"[PracticeModeManager] Player Instantiated. Active: {playerGO.activeSelf}, Layer: {LayerMask.LayerToName(playerGO.layer)} ({playerGO.layer}), Scale: {playerGO.transform.localScale}");

                // [DEBUG] Check FOVController status
                var fov = playerGO.GetComponent<FOVController>();
                if (fov != null && !fov.enabled) 
                {
                    Debug.LogWarning("[PracticeModeManager] FOVController found but disabled initially.");
                }

                InstanceFinder.ServerManager.Spawn(playerGO, targetConn);
                Debug.Log($"[PracticeModeManager] Player spawned for connection {targetConn.ClientId}");
                
                Camera cam = Camera.main;
                if (cam != null)
                {
                    Debug.Log($"[PracticeModeManager] Player spawned. Camera found: {cam.name}");
                }
                else
                {
                    Debug.LogWarning("[PracticeModeManager] No MainCamera found via tag!");
                }

                if (_spawnedGameStateManager != null)
                {
                    _spawnedGameStateManager.OnPlayerSpawnedAlive();
                    // Just to be sure, trigger player join/spawn logic if needed.
                    // GameStateManager.OnPlayerJoined() usually called by NetworkManager.
                    // Since we don't have NetworkManager logic here, we call it manually?
                    // NetworkManager calling OnPlayerJoined.
                    _spawnedGameStateManager.OnPlayerJoined();
                }
            }
            else
            {
                Debug.LogError("[PracticeModeManager] No client connection found to spawn player for!");
            }
        }
    }
}
