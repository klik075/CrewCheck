using System;
using UnityEngine;

namespace WhoisntCitizen.Network.Realtime
{
    /// <summary>
    /// WebSocket 메시지(JSON) ↔ C# 객체 변환.
    ///  - 받기: TryDecode — 서버 JSON을 RealtimeServerMessage 하위 클래스로. 실패해도 예외 없이 false.
    ///  - 보내기: EncodePing / EncodeSubscribe / EncodeUnsubscribe — 서버 ClientMessage 모양의 JSON 문자열.
    ///
    /// 서버 메시지는 type 하나에 필드 몇 개뿐인 평평한 객체라서, 모든 필드를 가진 Envelope 하나로 읽은 뒤
    /// type에 맞는 필드만 골라 쓴다. (JsonUtility는 다형성을 지원하지 않는다)
    /// JsonUtility는 없는 숫자 필드를 0으로 채우므로, 0이면 안 되는 값(roomId 등)은 직접 검사한다.
    /// </summary>
    public static class RealtimeMessageCodec
    {
        // 필드 이름은 서버 JSON 키와 같아야 한다. JsonUtility가 채우므로 직접 쓰지 않는다.
#pragma warning disable 0649
        [Serializable]
        private class Envelope
        {
            public string type;
            public long userId;
            public long heartbeatSeconds;
            public long idleTimeoutSeconds;
            public long serverTime;
            public long roomId;
            public string gameId;
            public long phaseVersion;
            public long lastMessageId;
            public string code;
            public string message;
        }
#pragma warning restore 0649

        /// <summary>
        /// 서버가 보낸 JSON 한 개를 해석한다.
        /// 성공하면 true와 message. 모르는 type은 실패가 아니라 UnknownServerMessage로 돌려준다.
        /// JSON이 깨졌거나, type이 없거나, 꼭 필요한 값이 빠졌으면 false와 error(이유).
        /// </summary>
        public static bool TryDecode(string json, out RealtimeServerMessage message, out string error)
        {
            message = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "빈 메시지입니다.";
                return false;
            }

            string trimmed = json.Trim();
            if (trimmed[0] != '{')
            {
                error = "JSON 객체가 아닙니다.";
                return false;
            }

            Envelope e;
            try
            {
                e = JsonUtility.FromJson<Envelope>(trimmed);
            }
            catch (Exception ex)
            {
                error = "JSON을 읽지 못했습니다: " + ex.Message;
                return false;
            }

            if (e == null || string.IsNullOrEmpty(e.type))
            {
                error = "type이 없습니다.";
                return false;
            }

            switch (e.type)
            {
                case ServerMessageTypes.Connected:
                    if (e.userId <= 0) return Fail(e.type, "userId", out error);
                    message = new ConnectedMessage(e.userId, e.heartbeatSeconds, e.idleTimeoutSeconds);
                    return true;

                case ServerMessageTypes.Pong:
                    message = new PongMessage(e.serverTime);
                    return true;

                case ServerMessageTypes.Subscribed:
                    if (e.roomId <= 0) return Fail(e.type, "roomId", out error);
                    message = new SubscribedMessage(e.roomId);
                    return true;

                case ServerMessageTypes.GameChanged:
                    if (string.IsNullOrEmpty(e.gameId)) return Fail(e.type, "gameId", out error);
                    message = new GameChangedMessage(e.gameId, e.phaseVersion);
                    return true;

                case ServerMessageTypes.RoomChanged:
                    if (e.roomId <= 0) return Fail(e.type, "roomId", out error);
                    message = new RoomChangedMessage(e.roomId);
                    return true;

                case ServerMessageTypes.RoomCreated:
                    if (e.roomId <= 0) return Fail(e.type, "roomId", out error);
                    message = new RoomCreatedMessage(e.roomId);
                    return true;

                case ServerMessageTypes.ChatChanged:
                    if (e.roomId <= 0) return Fail(e.type, "roomId", out error);
                    message = new ChatChangedMessage(e.roomId, e.lastMessageId);
                    return true;

                case ServerMessageTypes.Error:
                    if (string.IsNullOrEmpty(e.code)) return Fail(e.type, "code", out error);
                    message = new RealtimeErrorMessage(e.code, e.message);
                    return true;

                default:
                    message = new UnknownServerMessage(e.type);
                    return true;
            }
        }

        /// <summary>{"type":"ping"}</summary>
        public static string EncodePing()
        {
            return "{\"type\":\"" + ClientMessageTypes.Ping + "\"}";
        }

        /// <summary>{"type":"room.subscribe","roomId":3} — 이 방의 신호를 받겠다.</summary>
        public static string EncodeSubscribe(long roomId)
        {
            return EncodeRoomMessage(ClientMessageTypes.RoomSubscribe, roomId);
        }

        /// <summary>{"type":"room.unsubscribe","roomId":3} — 이 방의 신호를 그만 받겠다.</summary>
        public static string EncodeUnsubscribe(long roomId)
        {
            return EncodeRoomMessage(ClientMessageTypes.RoomUnsubscribe, roomId);
        }

        private static string EncodeRoomMessage(string type, long roomId)
        {
            if (roomId <= 0)
            {
                // 서버도 roomId(양수)가 없으면 BAD_MESSAGE로 거절한다. 보내기 전에 막는다.
                throw new ArgumentOutOfRangeException(nameof(roomId), roomId, "roomId는 양수여야 합니다.");
            }
            // long.ToString()은 문화권과 상관없이 숫자만 나온다.
            return "{\"type\":\"" + type + "\",\"roomId\":" + roomId + "}";
        }

        private static bool Fail(string type, string field, out string error)
        {
            error = type + "에 " + field + "가 없거나 올바르지 않습니다.";
            return false;
        }
    }
}
