using System;
using System.Text;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Utils;
using VRageMath;
using Draygo.API;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
using ProtoBuf;

namespace SFTybo.ChatBubbles
{
    [MySessionComponentDescriptor(MyUpdateOrder.AfterSimulation)]
    public class ChatBubbles : MySessionComponentBase
    {
        HudAPIv2 hud_base;
        //List<HudAPIv2.SpaceMessage> all_messages = new List<HudAPIv2.SpaceMessage>();
        StringBuilder text = new StringBuilder("");
        MyStringId bubble_texture;
        HudAPIv2.BillBoardHUDMessage billboard;
        int timer = 0;
        ushort id = 1049;
        Packet packet = new Packet();

        [ProtoContract]
        public class Packet
        {
            [ProtoMember(20)]
            public long steamId;

            [ProtoMember(21)]
            public string msg;

            public Packet()
            {

            }

            public Packet(long steamId, string msg)
            {
                this.steamId = steamId;
                this.msg = msg;
            }
        }

        public class MessageList
        {
            public long steamId;
            public List<HudAPIv2.SpaceMessage> all_other_messages = new List<HudAPIv2.SpaceMessage>();

            public MessageList()
            {

            }

            public MessageList(long steamId, List<HudAPIv2.SpaceMessage> all_other_messages)
            {
                this.steamId = steamId;
                this.all_other_messages = all_other_messages;
            }
        }
        List<MessageList> _msgsList = new List<MessageList>();

        private IMyPlayer getPlayer(ulong steamId)
        {
            List<IMyPlayer> allPlayers = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(allPlayers);
            foreach (var player in allPlayers)
            {
                if (player.SteamUserId == steamId)
                {
                    return player;
                }
            }
            return null;
        }

        private void registerMessage(ushort ushortId, byte[] messageByte, ulong ulongId, bool reliable)
        {
            packet = MyAPIGateway.Utilities.SerializeFromBinary<Packet>(messageByte);
            if (packet != null && !MyAPIGateway.Utilities.IsDedicated)
            {
                //MyVisualScriptLogicProvider.SendChatMessage(packet.msg);
                ulong steamId = (ulong)packet.steamId;
                IMyPlayer sentPlayer = getPlayer(steamId);
                
                if (sentPlayer != null)
                {
                    createMessage(packet.msg, sentPlayer?.Character, steamId);
                }
            }
        }

        public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.MessageEntered += Utilities_MessageEntered;
                //MyAPIGateway.Utilities.MessageRecieved += Utilities_MessageRecieved;
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(id, registerMessage);
            }
        }

        private void createMessage(string messageText, IMyCharacter character, ulong steamId)
        {
            MessageList ourMsgPack = null;
            foreach (var msgPack in _msgsList)
            {
                //MyVisualScriptLogicProvider.SendChatMessage(msgPack.steamId + "::" + steamId);
                if ((ulong)msgPack.steamId == (ulong)steamId)
                {
                    ourMsgPack = msgPack;
                    break;
                }
            }
            if (ourMsgPack == null)
            {
                ourMsgPack = new MessageList();
                ourMsgPack.steamId = (long)steamId;
                _msgsList.Insert(0, ourMsgPack);
            }
            var control = character as Sandbox.Game.Entities.IMyControllableEntity;
            var cam_mat = MyAPIGateway.Session.Camera.WorldMatrix;
            double chatHeight = 0;

            for (int i = 0; i < ourMsgPack.all_other_messages.Count; i++)
            {
                var message = ourMsgPack.all_other_messages[i];
                if (message != null)
                {
                    if (i <= 3 && control.EnabledBroadcasting)
                    {
                        message.WorldPosition = character.WorldMatrix.Translation + character.WorldMatrix.Up * chatHeight;
                    }
                    else
                    {
                        message.Visible = false;
                    }
                }
            }

            HudAPIv2.SpaceMessage floating_message = new HudAPIv2.SpaceMessage();

            int columnWidth = 50;
            int maxMessageSize = 200;
            string sentence = messageText;
            string[] words = sentence.Split(' ');

            StringBuilder newSentence = new StringBuilder();

            string line = "";
            foreach (string word in words)
            {
                if ((line + word).Length > columnWidth)
                {
                    newSentence.AppendLine(line);
                    line = "";
                }

                line += string.Format("{0} ", word);
                if (newSentence.Length > maxMessageSize)
                {
                    line += " ...";
                    break;
                }
                else if (line.Length > columnWidth)
                {
                    line = line.Substring(0, columnWidth) + "...";
                    break;
                }
            }

            if (line.Length > 0)
                newSentence.AppendLine(line);

            int numLines = newSentence.ToString().Split('\n').Length - 1;

            floating_message.Message = newSentence;
            floating_message.Up = cam_mat.Up;
            floating_message.Left = cam_mat.Left;
            double messageScale = (double)numLines;
            floating_message.Scale = .2 - ((messageScale - 1) * .02);
            double heightScale = (floating_message.Scale - .1) / .1;
            chatHeight = ((2.0 + ((double)numLines * .25)) * heightScale);
            floating_message.WorldPosition = character.WorldMatrix.Translation + (character.WorldMatrix.Up * chatHeight);
            //MyVisualScriptLogicProvider.SendChatMessage(numLines + " lines " + floating_message.Scale + " height: " + chatHeight);
            floating_message.TimeToLive = Clamp(newSentence.ToString().Length * 25, 300, 1000);
            floating_message.TxtOrientation = HudAPIv2.TextOrientation.center;
            floating_message.Blend = BlendTypeEnum.LDR;

            if (billboard != null)
            {
                //billboard.DeleteMessage();
            }
            billboard = new HudAPIv2.BillBoardHUDMessage(bubble_texture, new Vector2D(0, 0), Vector4.Zero);
            ourMsgPack.all_other_messages.Insert(0, floating_message);
            //sendToOthers = true;
        }

        private void Utilities_MessageEntered(string messageText, ref bool sendToOthers)
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                if (MyAPIGateway.Session?.Player?.Character != null && hud_base.Heartbeat)
                {
                    packet.steamId = (long)MyAPIGateway.Session?.Player?.SteamUserId;
                    packet.msg = messageText;
                    MyAPIGateway.Multiplayer.SendMessageToOthers(id, MyAPIGateway.Utilities.SerializeToBinary(packet), true);
                    createMessage(messageText, MyAPIGateway.Session?.Player?.Character, MyAPIGateway.Session.Player.SteamUserId);
                }
            }
        }

        public override void LoadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                hud_base = new HudAPIv2(HudLoaded);
            }
            bubble_texture = MyStringId.GetOrCompute("chatbubble");
        }

        private void HudLoaded()
        {

        }

        /*private void Utilities_MessageRecieved(ulong arg1, string messageText)
        {
            //if (MyAPIGateway.Session.IsServer && arg1 != MyAPIGateway.Multiplayer.MyId){}
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                MyAPIGateway.Utilities.ShowMessage("da world", messageText + " userid: " + arg1.ToString());
                
            }
        }*/

        public static double Distance(Vector3D value1, Vector3D value2)
        {
            double num = value1.X - value2.X;
            double num2 = value1.Y - value2.Y;
            double num3 = value1.Z - value2.Z;
            return Math.Sqrt(num * num + num2 * num2 + num3 * num3);
        }

        public static int Clamp(int value1, int min, int max)
        {
            if(value1 > max)
            {
                value1 = max;
            } else if(value1 < min)
            {
                value1 = min;
            }
            return value1;
        }

        public override void Draw()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                if (MyAPIGateway.Session.Camera != null && !MyAPIGateway.Utilities.IsDedicated)
                {
                    foreach (var msgPack in _msgsList)
                    {
                        IMyPlayer sentPlayer = getPlayer((ulong)msgPack.steamId);
                        if (sentPlayer != null)
                        {
                            var character = sentPlayer.Character;
                            var player_mat = character.WorldMatrix;
                            var camera_mat = MyAPIGateway.Session.Camera.WorldMatrix;
                            var control = character as Sandbox.Game.Entities.IMyControllableEntity;
                            int lineOffSet = 0;
                            var inCockpit = sentPlayer.Controller?.ControlledEntity is IMyCockpit;

                            //Below was for checking if the character was in LOS and the billboard would be set to be always on the front of the screen but haven't found a method
                            //for billboards to do that yet
                            /*List<IHitInfo> _hitList = new List<IHitInfo>();
                            IHitInfo hitInfo = null;
                            MyAPIGateway.Physics.CastRay(MyAPIGateway.Session.Camera.Position, character.GetPosition(), _hitList, CollisionLayers.CharacterCollisionLayer);

                            bool seeCharacter = false;
                            for (int i = 0; i < _hitList.Count; i++)
                            {
                                hitInfo = _hitList[i];
                                var hit = hitInfo?.HitEntity;
                                if (hit == character)
                                {
                                    seeCharacter = true;
                                }
                            }
                            MyAPIGateway.Utilities.ShowNotification("Can See character " + seeCharacter, 1, "White");*/
                            //GetPlayers(List<IMyPlayer>);
                            for (int i = 0; i < msgPack.all_other_messages.Count; i++)
                            {
                                var message = msgPack.all_other_messages[i];
                                if (message != null)
                                {
                                    if (i < 3 && control.EnabledBroadcasting && !inCockpit)
                                    {
                                        int numLines = message.Message.ToString().Split('\n').Length;
                                        message.WorldPosition = player_mat.Translation + player_mat.Up * (2 + (numLines * .1) + (lineOffSet * .1) + (i * .25));
                                        lineOffSet += numLines;
                                        message.Up = camera_mat.Up;
                                        message.Left = camera_mat.Left;
                                    }
                                    else
                                    {
                                        message.Visible = false;
                                    }
                                    if (billboard != null)
                                    {
                                        Vector3D world_p = message.WorldPosition;
                                        Vector3D screen = MyAPIGateway.Session.Camera.WorldToScreen(ref world_p);
                                        billboard.Origin = new Vector2D(screen.X, screen.Y);
                                        double distance = Distance(world_p, screen);
                                        if (distance <= 50)
                                        {
                                            billboard.Scale = 0.2f;
                                        }
                                        else
                                        {
                                            billboard.Scale = 0;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        protected override void UnloadData()
        {
            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                if (hud_base != null)
                {
                    hud_base.Unload();
                }
                MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(id, registerMessage);
                MyAPIGateway.Utilities.MessageEntered -= Utilities_MessageEntered;
                //MyAPIGateway.Utilities.MessageRecieved -= Utilities_MessageRecieved;
            }
        }
    }
}
