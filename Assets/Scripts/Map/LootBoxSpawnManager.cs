using Fusion;
using UnityEngine;
using System.Collections.Generic;

namespace ProjectVoid.Map
{
    /// <summary>
    /// 맵의 모든 LootBox 스폰을 중앙에서 관리하는 네트워크 매니저
    /// 각 청크의 LootBoxSpawnPoint 태그를 찾아서 LootBox를 스폰하고 관리합니다.
    /// </summary>
    public class LootBoxSpawnManager : NetworkBehaviour
    {
        #region Serialized Fields

        [Header("LootBox 설정")]
        [Tooltip("스폰할 LootBox 프리팹")]
        [SerializeField] private NetworkPrefabRef _lootBoxPrefab;

        [Tooltip("LootBox가 열린 후 다시 스폰될 때까지의 대기 시간 (초)")]
        [SerializeField] private float _lootBoxRespawnTime = 30f;

        [Header("디버그")]
        [Tooltip("스폰 정보 로그 출력")]
        [SerializeField] private bool _enableDebugLogs = true;

        #endregion

        #region Private Fields

        private List<NetworkObject> _spawnedLootBoxes = new List<NetworkObject>();
        private int _totalSpawnPointsFound = 0;
        private Transform _lootBoxParent;

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

            if (!_lootBoxPrefab.IsValid)
            {
                Debug.LogError("[LootBoxSpawnManager] LootBox 프리팹이 설정되지 않았습니다!");
                return;
            }

            // 기존 LootBox 제거
            DespawnAllLootBoxes();

            _totalSpawnPointsFound = 0;
            int spawnedCount = 0;
            var processedChunks = new HashSet<ChunkInstance>();

            // 모든 청크를 순회하면서 스폰 포인트 찾기
            foreach (var kvp in mapGenerator.PlacedChunks)
            {
                ChunkInstance chunk = kvp.Value;

                // 그룹 청크는 여러 칸에 같은 ChunkInstance가 있으므로 중복 방지
                if (processedChunks.Contains(chunk))
                    continue;

                processedChunks.Add(chunk);

                // 청크 내 모든 Transform 검색
                Transform[] allTransforms = chunk.GetComponentsInChildren<Transform>();

                foreach (Transform spawnPoint in allTransforms)
                {
                    // 자기 자신 제외
                    if (spawnPoint == chunk.transform)
                        continue;

                    // LootBoxSpawnPoint 태그 확인
                    if (spawnPoint.CompareTag("LootBoxSpawnPoint"))
                    {
                        _totalSpawnPointsFound++;

                        // Why: 월드 좌표 명시적으로 사용
                        Vector3 worldPosition = spawnPoint.position;
                        Quaternion worldRotation = spawnPoint.rotation;

                        // Why: onBeforeSpawned 콜백에서 부모 설정 및 리셋 시간 설정
                        NetworkObject lootBox = Runner.Spawn(
                            _lootBoxPrefab,
                            worldPosition,
                            worldRotation,
                            onBeforeSpawned: (runner, obj) =>
                            {
                                // Why: 스폰 전에 부모 설정하여 초기 동기화에 포함
                                if (_lootBoxParent != null)
                                {
                                    obj.transform.SetParent(_lootBoxParent, false);
                                    // Why: SetParent(false) 후 월드 위치 명시적 설정
                                    obj.transform.position = worldPosition;
                                    obj.transform.rotation = worldRotation;
                                }

                                // Why: LootBox의 리셋 시간을 Manager의 설정값으로 설정
                                LootBox lootBoxComponent = obj.GetComponent<LootBox>();
                                if (lootBoxComponent != null)
                                {
                                    lootBoxComponent.SetResetTime(_lootBoxRespawnTime);
                                }
                            }
                        );

                        if (lootBox != null)
                        {
                            _spawnedLootBoxes.Add(lootBox);
                            spawnedCount++;
                        }
                        else
                        {
                            Debug.LogError($"[LootBoxSpawnManager] LootBox 스폰 실패: {spawnPoint.name}");
                        }
                    }
                }
            }

            if (_totalSpawnPointsFound == 0)
            {
                Debug.LogWarning($"[LootBoxSpawnManager] 맵에서 'LootBoxSpawnPoint' 태그를 가진 오브젝트를 찾을 수 없습니다!");
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
        }

        #endregion

        #region Fusion Lifecycle

        public override void Spawned()
        {
            if (HasStateAuthority)
            {
                // Why: LootboxParent를 일반 GameObject로 생성
                SpawnLootBoxParent();
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            // Why: 매니저가 제거될 때 모든 LootBox 제거
            if (hasState && HasStateAuthority)
            {
                DespawnAllLootBoxes();

                // Why: LootBoxParent 일반 GameObject 제거
                if (_lootBoxParent != null)
                {
                    Destroy(_lootBoxParent.gameObject);
                }
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// LootboxParent를 일반 GameObject로 생성합니다.
        /// </summary>
        private void SpawnLootBoxParent()
        {
            if (!HasStateAuthority)
                return;

            // Why: 이미 생성되어 있으면 다시 생성하지 않음
            if (_lootBoxParent != null)
                return;

            GameObject parentObj = new GameObject("LootboxParent");
            _lootBoxParent = parentObj.transform;

            if (_enableDebugLogs)
            {
                Debug.Log("[LootBoxSpawnManager] LootBoxParent 생성 완료");
            }
        }

        #endregion
    }
}
