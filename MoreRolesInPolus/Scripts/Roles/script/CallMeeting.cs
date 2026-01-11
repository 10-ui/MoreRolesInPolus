using System;
using System.Collections.Generic;
using System.Text;

namespace MoreRolesInPolus.Scripts.Roles.script
{
    public static class CallMeetingHelper
    {
        [NebulaRPC]
        public static void CallMeeting(GamePlayer p)
        {
            if (AmongUsClient.Instance.AmHost)
            {
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
    }
}
