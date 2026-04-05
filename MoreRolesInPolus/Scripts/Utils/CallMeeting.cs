using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MoreRolesInPolus.Scripts.Utils
{
    public static class CallMeetingHelper
    {
        private static DateTime lastMeetingCallTime = DateTime.MinValue;

        [NebulaRPC]
        public static void CallMeeting(GamePlayer p, bool nowmeeting)
        {
            if (AmongUsClient.Instance.AmHost && !nowmeeting)
            {
                if ((DateTime.UtcNow - lastMeetingCallTime).TotalSeconds < 5.0) return;
                lastMeetingCallTime = DateTime.UtcNow;

                var player = PlayerControl.AllPlayerControls.GetFastEnumerator().FirstOrDefault(c => c.PlayerId == p.PlayerId);
                MeetingRoomManager.Instance.AssignSelf(player, null);
                if (GameManager.Instance.CheckTaskCompletion())
                {
                    return;
                }
                DestroyableSingleton<HudManager>.Instance.OpenMeetingRoom(player);
                player.RpcStartMeeting(null);
            }
        }

        [NebulaRPC]
        public static void CallMeetingPreferDeadBody(GamePlayer p, bool nowmeeting)
        {
            if (!AmongUsClient.Instance.AmHost || nowmeeting) return;
            if ((DateTime.UtcNow - lastMeetingCallTime).TotalSeconds < 5.0) return;
            lastMeetingCallTime = DateTime.UtcNow;

            var allControls = PlayerControl.AllPlayerControls
                .ToArray()
                .Where(control => control != null && control.Data != null)
                .ToList();
            if (allControls.Count == 0) return;

            var aliveControls = allControls
                .Where(control => !control.Data.IsDead)
                .ToList();
            if (aliveControls.Count == 0) return;

            var deadControls = allControls
                .Where(control => control.Data.IsDead)
                .ToList();

            PlayerControl reporter;
            NetworkedPlayerInfo? reportTarget = null;

            if (deadControls.Count > 0)
            {
                // 死体がある場合: 死体側からランダムに1人選ぶ
                // ただし会議種別は「死体通報」ではなく「ボタン会議」扱いにする
                var deadReporter = deadControls[System.Random.Shared.Next(deadControls.Count)];
                reporter = deadReporter;
            }
            else
            {
                // 全員生存の場合: 生存者ランダムがボタンを押す（緊急会議）
                reporter = aliveControls[System.Random.Shared.Next(aliveControls.Count)];
            }

            MeetingRoomManager.Instance.AssignSelf(reporter, reportTarget);
            if (GameManager.Instance.CheckTaskCompletion())
            {
                return;
            }
            DestroyableSingleton<HudManager>.Instance.OpenMeetingRoom(reporter);
            reporter.RpcStartMeeting(reportTarget);
        }
    }
}
