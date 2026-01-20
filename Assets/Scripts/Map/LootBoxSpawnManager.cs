using FishNet.Object;
using UnityEngine;
using System.Collections.Generic;

namespace ProjectVoid.Map
{
    /// <summary>
    /// LootBox 중앙 스폰 관리자
    /// </summary>
    public class LootBoxSpawnManager : NetworkBehaviour
    {
        #region Serialized Fields

        [Header("LootBox 설정")]
        [Tooltip("기본 LootBox 프리팹 (미설정 시 사용)")]
        [SerializeField] private NetworkObject _defaultLootBoxPrefab;

        [Tooltip("LootBox 리스폰 대기 시간 (초)")]
        [SerializeField] private float _defaultLootBoxRespawnTime = 30f;

        [Header("디버그")]
        [Tooltip("스폰 정보 로그 출력")]
        [SerializeField] private bool _enableDebugLogs = true;

        #endregion

        #region Private Fields

        private List<NetworkObject> _spawnedLootBoxes = new List<NetworkObject>();
        private int _totalSpawnPointsFound = 0;
        private Transform _lootBoxParent;
        private Transform _droppedItemsParent;

        #endregion

        #region Properties

        public int SpawnedLootBoxCount => _spawnedLootBoxes.Count;
        public int TotalSpawnPointsFound => _totalSpawnPointsFound;

        #endregion

        #region Fishnet Lifecycle

        public override void OnStartServer()
        {
            base.OnStartServer();
            _spawnedLootBoxes.Clear();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            
            DespawnAllLootBoxes();

            if (_lootBoxParent != null)
            {
                Destroy(_lootBoxParent.gameObject);
            }
            if (_droppedItemsParent != null)
            {
                Destroy(_droppedItemsParent.gameObject);
            }
        }

        #endregion

        #region Initialization

        private void SpawnLootBoxParent()
        {
            if (!IsServerInitialized) return;
            if (_lootBoxParent != null) return;

            GameObject lootboxParentObj = new GameObject("LootboxParent");
            _lootBoxParent = lootboxParentObj.transform;

            GameObject droppedItemsParentObj = new GameObject("DroppedItemsParent");
            _droppedItemsParent = droppedItemsParentObj.transform;

            LogDebug("LootBoxParent 및 DroppedItemsParent 생성 완료");
        }

        #endregion

        #region Public Methods

        public void SpawnLootBoxesForAllChunks(MapGenerator mapGenerator)
        {
            if (!IsServerInitialized)
            {
                Debug.LogWarning("[LootBoxSpawnManager] SpawnLootBoxesForAllChunks는 서버만 호출할 수 있습니다!");
                return;
            }

            if (mapGenerator == null)
            {
                Debug.LogError("[LootBoxSpawnManager] MapGenerator가 null입니다!");
                return;
            }

            DespawnAllLootBoxes();
            SpawnLootBoxParent();

            _totalSpawnPointsFound = 0;
            int spawnedCount = 0;
            int invalidCount = 0;
            var processedChunks = new HashSet<ChunkInstance>();

            foreach (var kvp in mapGenerator.PlacedChunks)
            {
                ChunkInstance chunk = kvp.Value;

                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);

                LootBoxSpawnPoint[] spawnPoints = chunk.GetComponentsInChildren<LootBoxSpawnPoint>();

                foreach (LootBoxSpawnPoint spawnPoint in spawnPoints)
                {
                    _totalSpawnPointsFound++;

                    if (spawnPoint.IsValid)
                    {
                        SpawnLootBoxAtPoint(spawnPoint);
                        spawnedCount++;
                    }
                    else if (spawnPoint.IsEnabled && _defaultLootBoxPrefab != null)
                    {
                        SpawnLootBoxAtPointWithDefault(spawnPoint);
                        spawnedCount++;
                    }
                    else
                    {
                        invalidCount++;
                        Debug.LogWarning($"[LootBoxSpawnManager] 유효하지 않은 스폰 포인트: {spawnPoint.gameObject.name}");
                    }
                }
            }

            if (_totalSpawnPointsFound == 0)
            {
                Debug.LogWarning("[LootBoxSpawnManager] 맵에서 LootBoxSpawnPoint를 찾을 수 없습니다!");
            }
            else
            {
                LogDebug($"LootBox 스폰 완료! 스폰 포인트: {_totalSpawnPointsFound}개, 스폰됨: {spawnedCount}개");
            }
        }

        public void DespawnAllLootBoxes()
        {
            if (!IsServerInitialized)
            {
                Debug.LogWarning("[LootBoxSpawnManager] DespawnAllLootBoxes는 서버만 호출할 수 있습니다!");
                return;
            }

            foreach (NetworkObject lootBox in _spawnedLootBoxes)
            {
                if (lootBox != null && lootBox.IsSpawned)
                {
                    ServerManager.Despawn(lootBox);
                }
            }

            _spawnedLootBoxes.Clear();
            LogDebug("모든 LootBox 제거됨.");
        }

        #endregion

        #region Spawn Methods

        private void SpawnLootBoxAtPoint(LootBoxSpawnPoint spawnPoint)
        {
            Vector3 worldPosition = spawnPoint.transform.position;
            Quaternion worldRotation = spawnPoint.transform.rotation;
            float respawnTime = spawnPoint.GetRespawnTime(_defaultLootBoxRespawnTime);

            // Fishnet: Instantiate 후 Spawn
            NetworkObject lootBoxPrefab = spawnPoint.LootBoxPrefab.GetComponent<NetworkObject>();
            if (lootBoxPrefab == null)
            {
                Debug.LogError($"[LootBoxSpawnManager] {spawnPoint.LootBoxPrefab.name}에 NetworkObject가 없습니다!");
                return;
            }

            NetworkObject lootBox = Instantiate(lootBoxPrefab, worldPosition, worldRotation);

            if (_lootBoxParent != null)
            {
                lootBox.transform.SetParent(_lootBoxParent, true);
            }

            LootBox lootBoxComponent = lootBox.GetComponent<LootBox>();
            if (lootBoxComponent != null)
            {
                lootBoxComponent.SetResetTime(respawnTime);
            }

            ServerManager.Spawn(lootBox);

            if (lootBox != null)
            {
                // Ensure correct layer
                int boxLayer = LayerMask.NameToLayer("LootBox");
                if (boxLayer != -1)
                {
                    SetLayerRecursively(lootBox.gameObject, boxLayer);
                }

                _spawnedLootBoxes.Add(lootBox);
                LogDebug($"LootBox 스폰: {spawnPoint.gameObject.name} at {worldPosition}");
            }
        }

        private void SpawnLootBoxAtPointWithDefault(LootBoxSpawnPoint spawnPoint)
        {
            Vector3 worldPosition = spawnPoint.transform.position;
            Quaternion worldRotation = spawnPoint.transform.rotation;
            float respawnTime = spawnPoint.GetRespawnTime(_defaultLootBoxRespawnTime);

            NetworkObject lootBox = Instantiate(_defaultLootBoxPrefab, worldPosition, worldRotation);

            if (_lootBoxParent != null)
            {
                lootBox.transform.SetParent(_lootBoxParent, true);
            }

            LootBox lootBoxComponent = lootBox.GetComponent<LootBox>();
            if (lootBoxComponent != null)
            {
                lootBoxComponent.SetResetTime(respawnTime);
            }

            ServerManager.Spawn(lootBox);

            if (lootBox != null)
            {
                // Ensure correct layer
                int boxLayer = LayerMask.NameToLayer("LootBox");
                if (boxLayer != -1)
                {
                    SetLayerRecursively(lootBox.gameObject, boxLayer);
                }

                _spawnedLootBoxes.Add(lootBox);
                LogDebug($"LootBox 스폰 (기본 프리팹): {spawnPoint.gameObject.name} at {worldPosition}");
            }
        }

        private void SetLayerRecursively(GameObject obj, int newLayer)
        {
            obj.layer = newLayer;
            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, newLayer);
            }
        }


        #endregion

        #region Helper Methods

        private void LogDebug(string message)
        {
            if (_enableDebugLogs)
            {
                Debug.Log($"[LootBoxSpawnManager] {message}");
            }
        }

        #endregion
    }
}
