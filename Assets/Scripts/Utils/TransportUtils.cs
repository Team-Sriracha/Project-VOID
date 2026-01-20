using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

namespace ProjectVoid.Core
{
    /// <summary>
    /// FishNet Transport 관련 유틸리티 클래스
    /// Transport 전환 및 설정 로직 담당
    /// </summary>
    public static class TransportUtils
    {
        /// <summary>
        /// 지정된 NetworkManager의 Transport를 Yak(Offline/Direct)으로 전환
        /// </summary>
        /// <param name="nm">대상 NetworkManager</param>
        /// <returns>전환 성공 여부</returns>
        public static bool SwitchToYakTransport(FishNet.Managing.NetworkManager nm)
        {
            if (nm == null)
            {
                Debug.LogError("[TransportUtils] NetworkManager is null");
                return false;
            }

            try
            {
                // TransportManager 확인
                var transportManager = nm.TransportManager;
                if (transportManager == null)
                {
                    Debug.LogWarning("[TransportUtils] TransportManager not found");
                    return false;
                }

                // 사용 중인 Transport가 있다면 정리

                var currentTransport = transportManager.Transport;
                if (currentTransport != null)
                {
                    Debug.Log($"[TransportUtils] Shutting down current transport: {currentTransport.GetType().Name}");
                    currentTransport.Shutdown();
                }

                // Yak Transport 컴포넌트 직접 찾기 (Fish-Net Pro 기능 활용)
                var yakTransport = nm.GetComponent<FishNet.Transporting.Yak.Yak>();
                if (yakTransport == null)
                {
                    Debug.LogWarning("[TransportUtils] Yak Transport component not found on NetworkManager. Make sure Yak is added to the NetworkManager GameObject.");
                    return false;
                }

                // 이전 Transport 구독 해제

                nm.ServerManager.SubscribeToTransport(false);
                nm.ClientManager.SubscribeToEvents(false);

                // Transport 교체 및 초기화
                transportManager.Transport = yakTransport;
                
                // Yak Transport 초기화 (인덱스 0 가정)
                yakTransport.Initialize(nm, 0);

                // 새 Transport에 매니저 구독
                Debug.Log("[TransportUtils] Subscribing Managers to new Yak Transport...");
                nm.ServerManager.SubscribeToTransport(true);
                nm.ClientManager.SubscribeToEvents(true);
                
                Debug.Log("[TransportUtils] Switched to Yak Transport successfully!");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[TransportUtils] Failed to switch to Yak Transport: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }
    }
}
