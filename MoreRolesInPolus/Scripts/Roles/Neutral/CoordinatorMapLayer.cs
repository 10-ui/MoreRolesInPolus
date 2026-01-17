/**
 * CoordinatorMapLayer.cs
 * 
 * 概要: Coordinator役職用のマップレイヤー。マップ上をクリックして部屋を選択する。
 * 仕様:
 *   - MonoBehaviour を継承
 *   - マウス追従ドット（常に緑表示）を提供
 *   - クリック位置からSystemTypesを判定し、コールバックを呼び出す
 *   - Doppelgangerスタイルのビジュアル（ターゲットマーカー）を表示
 * 制限:
 *   - 部屋のCollider判定に依存するため、Colliderがない廊下等はSystemTypes.Hallwaysに判定される
 */
using UnityEngine;
using Nebula.Utilities;
using Nebula.Map;
using Nebula.Extensions;
using Il2CppInterop.Runtime.Injection;
using BepInEx.Unity.IL2CPP.Utils;

using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;
using Color = UnityEngine.Color;

namespace MoreRolesInPolus.Roles.Neutral
{
    /// <summary>
    /// Coordinatorコールバック用インターフェース (IL2CPP互換)
    /// Coordinator.Instance クラスで実装される
    /// </summary>
    public interface ICoordinatorMapCallback
    {
        void OnRoomSelected(SystemTypes room, Vector2 clickedWorldPos);
    }

    /// <summary>
    /// Coordinator用マップレイヤークラス
    /// FakePlayerMapLayerの実装を参考に、独自に実装
    /// </summary>
    public class CoordinatorMapLayer : MonoBehaviour
    {
        // IL2CPP Registration
        static CoordinatorMapLayer()
        {
            ClassInjector.RegisterTypeInIl2Cpp<CoordinatorMapLayer>();
        }

        /// <summary>
        /// コールバック用参照
        /// </summary>
        private ICoordinatorMapCallback callbackHandler;

        /// <summary>
        /// マウス追従ドット用スプライトレンダラー
        /// </summary>
        private SpriteRenderer dotRenderer;

        /// <summary>
        /// クリック判定用Collider
        /// </summary>
        private CircleCollider2D clickCollider;

        /// <summary>
        /// クリックボタン
        /// </summary>
        private PassiveButton clickButton;

        /// <summary>
        /// ターゲットマーカー用スプライトレンダラー
        /// </summary>
        private SpriteRenderer targetMarker;

        /// <summary>
        /// マップ中心座標
        /// </summary>
        private Vector2 mapCenter;

        /// <summary>
        /// マップスケール
        /// </summary>
        private float mapScale;

        /// <summary>
        /// 初期化処理 (IL2CPP互換: インターフェース参照を使用)
        /// </summary>
        /// <param name="handler">部屋選択時のコールバックハンドラ</param>
        public void InjectCallback(ICoordinatorMapCallback handler)
        {
            UnityEngine.Debug.Log("CoordinatorMapLayer: InjectCallback called");
            this.callbackHandler = handler;
        }

        /// <summary>
        /// Awake時にUI要素を生成
        /// </summary>
        private void Awake()
        {
            UnityEngine.Debug.Log("CoordinatorMapLayer: Awake called");
            
            // マップ情報を取得
            mapCenter = VanillaAsset.GetMapCenter(AmongUsUtil.CurrentMapId);
            mapScale = VanillaAsset.GetMapScale(AmongUsUtil.CurrentMapId);

            // マウス追従ドットを作成
            dotRenderer = UnityHelper.CreateObject<SpriteRenderer>("DotRenderer", transform, new Vector3(0f, 0f, -25f), null);
            dotRenderer.sprite = MapBehaviour.Instance.HerePoint.sprite;
            dotRenderer.color = Color.green;
            dotRenderer.transform.localScale = Vector3.one * 0.45f;

            // クリック判定用Colliderを作成
            var clickObj = new GameObject("Click");
            clickObj.transform.SetParent(transform);
            clickObj.transform.localPosition = new Vector3(0f, 0f, -5f);
            clickCollider = clickObj.AddComponent<CircleCollider2D>();
            clickCollider.radius = 2f;
            clickCollider.isTrigger = true;

            // クリックボタンを設定
            clickButton = clickObj.SetUpButton(false, (SpriteRenderer)null, null, null);
            clickButton.OnClick.AddListener((UnityEngine.Events.UnityAction)TryClickHere);

            // ターゲットマーカー（クリック時に表示）を作成
            targetMarker = UnityHelper.CreateObject<SpriteRenderer>("TargetMarker", transform, new Vector3(0f, 0f, -22f), null);
            targetMarker.sprite = MapBehaviour.Instance.HerePoint.sprite;
            targetMarker.color = new Color(229f / 255f, 151f / 255f, 150f / 255f, 0.8f); // Coordinator色
            targetMarker.transform.localScale = Vector3.one * 1.2f;
            targetMarker.enabled = false;
            
            UnityEngine.Debug.Log("CoordinatorMapLayer: Awake completed successfully");
        }

        /// <summary>
        /// Update時にマウス追従処理
        /// </summary>
        private void Update()
        {
            // マウス位置をワールド座標に変換
            Vector3 screenPosAsWorld = UnityHelper.ScreenToWorldPoint(Input.mousePosition, LayerExpansion.GetUILayer());
            Vector3 worldPosOnMinimap = transform.InverseTransformPoint(screenPosAsWorld);
            Vector2 worldPos = VanillaAsset.ConvertFromMinimapPosToWorld(worldPosOnMinimap, AmongUsUtil.CurrentMapId);

            // Colliderとドットの位置を更新
            worldPosOnMinimap.z = -5f;
            clickCollider.transform.localPosition = worldPosOnMinimap;
            worldPosOnMinimap.z = -25f;
            dotRenderer.transform.localPosition = worldPosOnMinimap;

            // マップ領域内なら緑、領域外なら赤
            bool isValidArea = Nebula.Map.MapData.GetCurrentMapData().CheckMapArea(worldPos, 0.2f);
            dotRenderer.color = isValidArea ? Color.green : Color.red;
        }

        /// <summary>
        /// クリック時の処理（FakePlayerMapLayerと同じ方式）
        /// </summary>
        private void TryClickHere()
        {
            Vector3 screenPosAsWorld = UnityHelper.ScreenToWorldPoint(Input.mousePosition, LayerExpansion.GetUILayer());
            Vector3 worldPosOnMinimap = transform.InverseTransformPoint(screenPosAsWorld);
            worldPosOnMinimap.z = -5f;
            Vector2 worldPos = VanillaAsset.ConvertFromMinimapPosToWorld(worldPosOnMinimap, AmongUsUtil.CurrentMapId);

            // マップ領域外のクリックは無視（FakePlayerMapLayerと同じ判定）
            if (!Nebula.Map.MapData.GetCurrentMapData().CheckMapArea(worldPos, 0.2f))
            {
                UnityEngine.Debug.Log("CoordinatorMapLayer: Click ignored (outside map area)");
                return;
            }

            UnityEngine.Debug.Log($"CoordinatorMapLayer: OnClick at world({worldPos.x}, {worldPos.y})");

            // クリック位置から部屋を判定
            SystemTypes selectedRoom = GetRoomAtPosition(worldPos);
            
            UnityEngine.Debug.Log($"CoordinatorMapLayer: Selected room = {selectedRoom}");

            // ターゲットマーカーを表示
            targetMarker.enabled = true;
            targetMarker.transform.localPosition = new Vector3(worldPosOnMinimap.x, worldPosOnMinimap.y, -22f);

            // クリック演出（白く光らせる）
            NebulaManager.Instance.StartCoroutine(CoClickEffect().WrapToIl2Cpp());

            // コールバック呼び出し (インターフェース経由、クリック位置も渡す)
            callbackHandler?.OnRoomSelected(selectedRoom, worldPos);
        }

        /// <summary>
        /// クリック時の視覚エフェクト
        /// </summary>
        private System.Collections.IEnumerator CoClickEffect()
        {
            if (targetMarker == null) yield break;

            // 白く光らせる
            targetMarker.color = Color.white;
            targetMarker.transform.localScale = Vector3.one * 1.5f;
            yield return new WaitForSeconds(0.15f);

            // 元に戻す
            targetMarker.color = new Color(229f / 255f, 151f / 255f, 150f / 255f, 0.8f);
            targetMarker.transform.localScale = Vector3.one * 1.2f;
        }

        /// <summary>
        /// ワールド座標から部屋タイプを判定する
        /// </summary>
        /// <param name="worldPos">判定するワールド座標</param>
        /// <returns>その座標が属する部屋のSystemTypes</returns>
        private SystemTypes GetRoomAtPosition(Vector2 worldPos)
        {
            // ShipStatusのFastRoomsから部屋を検索
            if (ShipStatus.Instance == null) return SystemTypes.Hallway;

            foreach (var kvp in ShipStatus.Instance.FastRooms)
            {
                var room = kvp.Value;
                if (room == null || room.roomArea == null) continue;

                // Collider2D.OverlapPointで判定
                if (room.roomArea.OverlapPoint(worldPos))
                {
                    return kvp.Key;
                }
            }

            // どの部屋にも属さない場合は廊下
            return SystemTypes.Hallway;
        }

        /// <summary>
        /// 非表示時の処理
        /// </summary>
        private void OnDisable()
        {
            // ターゲットマーカーを非表示
            if (targetMarker != null)
            {
                targetMarker.enabled = false;
            }
        }
    }
}
