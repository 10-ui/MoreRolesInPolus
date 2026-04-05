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
        private static void ShowScoreOnMeeting((byte playerId, int score, int pointsToWin) data, int remainingRetry)
        {
            if (!MeetingHud.Instance)
            {
                if (remainingRetry <= 0) return;
                NebulaManager.Instance.ScheduleDelayAction(() => ShowScoreOnMeeting(data, remainingRetry - 1));
                return;
            }

            // 進捗率を計算
            float progress = (float)data.score / data.pointsToWin * 100f;
            string displayText = $"Coordinator: {data.score}/{data.pointsToWin} ({progress:F0}%)";

            // HudManagerのテキストを複製して使用
            var textObj = new UnityEngine.GameObject("CoordinatorScoreDisplay");
            textObj.layer = LayerExpansion.GetUILayer();

            var text = textObj.AddComponent<TMPro.TextMeshPro>();
            text.font = VanillaAsset.VersionFont;
            text.fontSize = 2.6f;
            text.fontSizeMin = 2.0f;
            text.fontSizeMax = 3.0f;
            text.alignment = TMPro.TextAlignmentOptions.Left;
            text.text = $"<color=#E59796>{displayText}</color>";

            // MeetingHudExtensionを使って左上に追加
            MeetingHudExtension.AddLeftContent(textObj);

            // 100%到達後は審判会議に入るため、会議中TitleShowerの保険表示は出さない
            if (data.score < data.pointsToWin)
            {
                NebulaAPI.CurrentGame?.GetModule<TitleShower>()?.SetText(displayText, new UnityEngine.Color(229f / 255f, 151f / 255f, 150f / 255f), 3f, true);
            }
        }

        public static readonly RemoteProcess<(byte playerId, int score, int pointsToWin)> RpcShareScore = 
            new RemoteProcess<(byte playerId, int score, int pointsToWin)>("Coordinator.ShareScore", (data, _) =>
        {
            // MeetingHudがまだ生成されていないタイミングでも取りこぼさない
            ShowScoreOnMeeting(data, 30);
        });
    }
}
