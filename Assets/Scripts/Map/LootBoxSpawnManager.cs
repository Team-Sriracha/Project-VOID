using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 맵의 모든 LootBox 스폰을 중앙에서 관리하는 네트워크 매니저
    /// LootBoxSpawnPoint 컴포넌트를 찾아서 LootBox를 스폰하고 관리합니다.
    /// MobSpawnManager와 동일한 패턴을 사용합니다.
    /// </summary>
    public class LootBoxSpawnManager : NetworkBehaviour
    {
        #region Serialized Fields

        [Header("LootBox 설정")]
        [Tooltip("기본 스폰할 LootBox 프리팹 (LootBoxSpawnPoint에 프리팹이 없을 때 사용)")]
        [SerializeField] private NetworkPrefabRef _defaultLootBoxPrefab;

        [Tooltip("LootBox가 열린 후 다시 스폰될 때까지의 기본 대기 시간 (초)")]
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

        /// <summary>
        /// 현재 스폰된 LootBox 개수
        /// </summary>
        public int SpawnedLootBoxCount => _spawnedLootBoxes.Count;

        /// <summary>
        /// 발견된 총 스폰 포인트 개수
        /// </summary>
        public int TotalSpawnPointsFound => _totalSpawnPointsFound;

        #endregion

        #region Fusion Lifecycle

        public override void Spawned()
        {
            // Why: NetworkObject를 씬 전환 시에도 유지하려면 Runner.MakeDontDestroyOnLoad 사용
            // Why: 서버에서만 호출 - 클라이언트에서는 assertion 경고가 발생할 수 있음
            if (HasStateAuthority)
            {
                Runner.MakeDontDestroyOnLoad(gameObject);
                // Why: LootboxParent는 [GamePlay] 씬 로드 후에 생성 (SpawnLootBoxesForAllChunks에서)
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            // Why: 매니저가 제거될 때 모든 LootBox 제거
            if (hasState && HasStateAuthority)
            {
                DespawnAllLootBoxes();

                // Why: LootBoxParent 및 DroppedItemsParent 제거
                if (_lootBoxParent != null)
                {
                    Destroy(_lootBoxParent.gameObject);
                }
                if (_droppedItemsParent != null)
                {
                    Destroy(_droppedItemsParent.gameObject);
                }
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// LootboxParent와 DroppedItemsParent를 생성합니다.
        /// </summary>
        private void SpawnLootBoxParent()
        {
            if (!HasStateAuthority)
                return;

            // Why: 이미 생성되어 있으면 다시 생성하지 않음
            if (_lootBoxParent != null)
                return;

            // Why: LootBox들을 보관할 부모 생성
            GameObject lootboxParentObj = new GameObject("LootboxParent");
            _lootBoxParent = lootboxParentObj.transform;
            MoveToRunnerScene(lootboxParentObj);

            // Why: 드랍된 아이템들을 보관할 부모 생성
            GameObject droppedItemsParentObj = new GameObject("DroppedItemsParent");
            _droppedItemsParent = droppedItemsParentObj.transform;
            MoveToRunnerScene(droppedItemsParentObj);

            LogDebug("LootBoxParent 및 DroppedItemsParent 생성 완료");
        }

        /// <summary>
        /// Multi-Peer 환경에서 GameObject를 [GamePlay] 씬으로 이동합니다.
        /// </summary>
        private void MoveToRunnerScene(GameObject obj)
        {
            if (obj == null) return;
            if (Runner == null) return;
            
            // Why: Runner.SimulationUnityScene은 [GamePlay] 씬이 아닐 수 있음
            // [GamePlay] 씬의 루트 오브젝트를 찾아서 그 씬으로 이동
            Scene gameplayScene = default;
            
            if (Runner.SimulationUnityScene.IsValid())
            {
                foreach (GameObject rootObj in Runner.SimulationUnityScene.GetRootGameObjects())
                {
                    // Why: [GamePlay] 라는 이름을 가진 루트 오브젝트가 있는 씬이 GamePlay 씬
                    if (rootObj.name == "[GamePlay]" || rootObj.name == "GamePlay")
                    {
                        // Why: [GamePlay] 오브젝트의 자식으로 추가
                        obj.transform.SetParent(rootObj.transform, false);
                        LogDebug($"{obj.name}을(를) [GamePlay] 하위로 이동");
                        return;
                    }
                }
            }
            
            // Why: [GamePlay] 오브젝트를 찾지 못하면 Runner 씬으로 이동 (폴백)
            if (Runner.SimulationUnityScene.IsValid())
            {
                SceneManager.MoveGameObjectToScene(obj, Runner.SimulationUnityScene);
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// MapGenerator의 모든 청크에서 LootBox를 스폰합니다.
        /// 서버(State Authority)만 호출해야 합니다.
        /// </summary>
        /// <param name="mapGenerator">맵 생성기 인스턴스</param>
        public void SpawnLootBoxesForAllChunks(MapGenerator mapGenerator)
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[LootBoxSpawnManager] SpawnLootBoxesForAllChunks는 서버만 호출할 수 있습니다!");
                return;
            }

            if (mapGenerator == null)
            {
                Debug.LogError("[LootBoxSpawnManager] MapGenerator가 null입니다!");
                return;
            }

            // 기존 LootBox 제거
            DespawnAllLootBoxes();

            // Why: [GamePlay] 씬 로드 후에 LootboxParent 생성/이동
            SpawnLootBoxParent();

            _totalSpawnPointsFound = 0;
            int spawnedCount = 0;
            int invalidCount = 0;
            var processedChunks = new HashSet<ChunkInstance>();

            // 모든 청크를 순회하면서 LootBoxSpawnPoint 컴포넌트 찾기
            foreach (var kvp in mapGenerator.PlacedChunks)
            {
                ChunkInstance chunk = kvp.Value;

                // 그룹 청크는 여러 칸에 같은 ChunkInstance가 있으므로 중복 방지
                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);

                // 청크 내 모든 LootBoxSpawnPoint 컴포넌트 검색
                LootBoxSpawnPoint[] spawnPoints = chunk.GetComponentsInChildren<LootBoxSpawnPoint>();

                foreach (LootBoxSpawnPoint spawnPoint in spawnPoints)
                {
                    _totalSpawnPointsFound++;

                    if (spawnPoint.IsValid)
                    {
                        SpawnLootBoxAtPoint(spawnPoint);
                        spawnedCount++;
                    }
                    else if (spawnPoint.IsEnabled && _defaultLootBoxPrefab.IsValid)
                    {
                        // Why: 프리팹이 설정되지 않았지만 활성화된 경우, 기본 프리팹 사용
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
                Debug.LogWarning("[LootBoxSpawnManager] 맵에서 LootBoxSpawnPoint 컴포넌트를 가진 오브젝트를 찾을 수 없습니다!");
            }
            else
            {
                LogDebug($"LootBox 스폰 완료! 스폰 포인트: {_totalSpawnPointsFound}개, 스폰됨: {spawnedCount}개, 유효하지 않음: {invalidCount}개");
            }
        }

        /// <summary>
        /// 스폰된 모든 LootBox를 제거합니다.
        /// 서버(State Authority)만 호출해야 합니다.
        /// </summary>
        public void DespawnAllLootBoxes()
        {
            if (!HasStateAuthority)
            {
                Debug.LogWarning("[LootBoxSpawnManager] DespawnAllLootBoxes는 서버만 호출할 수 있습니다!");
                return;
            }

            if (Runner == null)
            {
                Debug.LogWarning("[LootBoxSpawnManager] Runner가 null입니다. LootBox를 제거할 수 없습니다.");
                return;
            }

            foreach (NetworkObject lootBox in _spawnedLootBoxes)
            {
                if (lootBox != null && lootBox.IsValid)
                {
                    Runner.Despawn(lootBox);
                }
            }

            _spawnedLootBoxes.Clear();
            LogDebug("모든 LootBox 제거됨.");
        }

        #endregion

        #region Spawn Methods

        /// <summary>
        /// LootBoxSpawnPoint에서 지정된 LootBox를 스폰합니다.
        /// </summary>
        /// <param name="spawnPoint">스폰 포인트 컴포넌트</param>
        private void SpawnLootBoxAtPoint(LootBoxSpawnPoint spawnPoint)
        {
            Vector3 worldPosition = spawnPoint.transform.position;
            Quaternion worldRotation = spawnPoint.transform.rotation;
            float respawnTime = spawnPoint.GetRespawnTime(_defaultLootBoxRespawnTime);

            NetworkObject lootBox = Runner.Spawn(
                spawnPoint.LootBoxPrefab,
                worldPosition,
                worldRotation,
                onBeforeSpawned: (runner, obj) =>
                {
                    // Why: 스폰 전에 부모 설정
                    if (_lootBoxParent != null)
                    {
                        obj.transform.SetParent(_lootBoxParent, false);
                        obj.transform.position = worldPosition;
                        obj.transform.rotation = worldRotation;
                    }

                    // Why: LootBox의 리셋 시간 설정
                    LootBox lootBoxComponent = obj.GetComponent<LootBox>();
                    if (lootBoxComponent != null)
                    {
                        lootBoxComponent.SetResetTime(respawnTime);
                    }
                }
            );

            if (lootBox != null)
            {
                _spawnedLootBoxes.Add(lootBox);
                LogDebug($"LootBox 스폰: {spawnPoint.gameObject.name} at {worldPosition}");
            }
            else
            {
                Debug.LogError($"[LootBoxSpawnManager] LootBox 스폰 실패: {spawnPoint.gameObject.name}");
            }
        }

        /// <summary>
        /// LootBoxSpawnPoint에서 기본 프리팹으로 LootBox를 스폰합니다.
        /// </summary>
        /// <param name="spawnPoint">스폰 포인트 컴포넌트</param>
        private void SpawnLootBoxAtPointWithDefault(LootBoxSpawnPoint spawnPoint)
        {
            Vector3 worldPosition = spawnPoint.transform.position;
            Quaternion worldRotation = spawnPoint.transform.rotation;
            float respawnTime = spawnPoint.GetRespawnTime(_defaultLootBoxRespawnTime);

            NetworkObject lootBox = Runner.Spawn(
                _defaultLootBoxPrefab,
                worldPosition,
                worldRotation,
                onBeforeSpawned: (runner, obj) =>
                {
                    if (_lootBoxParent != null)
                    {
                        obj.transform.SetParent(_lootBoxParent, false);
                        obj.transform.position = worldPosition;
                        obj.transform.rotation = worldRotation;
                    }

                    LootBox lootBoxComponent = obj.GetComponent<LootBox>();
                    if (lootBoxComponent != null)
                    {
                        lootBoxComponent.SetResetTime(respawnTime);
                    }
                }
            );

            if (lootBox != null)
            {
                _spawnedLootBoxes.Add(lootBox);
                LogDebug($"LootBox 스폰 (기본 프리팹): {spawnPoint.gameObject.name} at {worldPosition}");
            }
            else
            {
                Debug.LogError($"[LootBoxSpawnManager] LootBox 스폰 실패: {spawnPoint.gameObject.name}");
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
