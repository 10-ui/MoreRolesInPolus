using Nebula.Modules;
using Nebula.Extensions;
using Nebula.Utilities;
using Virial;
using Virial.Events.Game.Meeting;
using Virial.Game;

namespace MoreRolesInPolus.Helpers;

/// <summary>
/// Coordinator役職用のヘルパークラス
/// RPC通信やユーティリティ関数を提供
/// </summary>
public class CoordinatorHelpers
{
    [NebulaRPCHolder]
    public class RpcHolder
    {
        /// <summary>
        /// ターゲットに座標特定失敗を通知するRPC
        /// </summary>
        public static readonly RemoteProcess<byte> RpcNotifyTarget = new RemoteProcess<byte>("Coordinator.NotifyTarget", (targetId, _) =>
        {
            if (PlayerControl.LocalPlayer.PlayerId == targetId)
            {
                // Play sound
                NebulaAsset.PlaySE(NebulaAudioClip.ButtonBreaking, false, 1.0f);
                
                // Show HUD message
                NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText(
                    Language.Translate("coordinator.notification.failed"), 
                    UnityEngine.Color.red, 
                    5.0f, 
                    true
                );
                
                // Optional: Flash screen
                AmongUsUtil.PlayFlash(UnityEngine.Color.red);
            }
        });

        /// <summary>
        /// 会議開始時にスコアを全員に共有するRPC
        /// 左上にテキストを追加表示
        /// (playerId, score, pointsToWin)
        /// </summary>
        public static readonly RemoteProcess<(byte playerId, int score, int pointsToWin)> RpcShareScore = 
            new RemoteProcess<(byte playerId, int score, int pointsToWin)>("Coordinator.ShareScore", (data, _) =>
        {
            NebulaPlugin.Log.Print($"Coordinator.ShareScore received: score={data.score}/{data.pointsToWin}");

            if (!MeetingHud.Instance)
            {
                NebulaPlugin.Log.Print("Coordinator.ShareScore: MeetingHud.Instance is null, aborting");
                return;
            }

            // 進捗率を計算
            float progress = (float)data.score / data.pointsToWin * 100f;
            
            // HudManagerのテキストを複製して使用
            var textObj = new UnityEngine.GameObject("CoordinatorScoreDisplay");
            textObj.layer = LayerExpansion.GetUILayer();
            
            var text = textObj.AddComponent<TMPro.TextMeshPro>();
            text.font = VanillaAsset.VersionFont;
            text.fontSize = 2f;
            text.fontSizeMin = 1.5f;
            text.fontSizeMax = 2.5f;
            text.alignment = TMPro.TextAlignmentOptions.Left;
            text.text = $"<color=#E59796>Coordinator: {data.score}/{data.pointsToWin} ({progress:F0}%)</color>";
            
            // MeetingHudExtensionを使って左上に追加
            MeetingHudExtension.AddLeftContent(textObj);
            
            NebulaPlugin.Log.Print($"Coordinator.ShareScore: Text added to left content");
        });
    }
}
