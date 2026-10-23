using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhoisntCitizen.Network
{
    /// <summary>
    /// Unity JsonUtility 보조 함수 모음.
    ///
    /// JsonUtility의 제약
    ///   1) 최상위가 배열인 JSON("[ ... ]")을 바로 파싱하지 못한다.
    ///      → { "items": [ ... ] } 로 감싸서 파싱한다. (FromJsonArray)
    ///   2) 잘못된 JSON이면 예외를 던진다.
    ///      → 예외 대신 null을 돌려준다. (FromJson)
    ///   3) enum을 문자열로 읽지 못한다.
    ///      → DTO에서 enum 값(status 등)은 string 필드로 받는다.
    /// </summary>
    public static class JsonHelper
    {
        // 배열을 감싸기 위한 임시 클래스. JsonUtility는 [Serializable] 클래스만 파싱할 수 있다.
        [Serializable]
        private class Wrapper<T>
        {
            public List<T> items;
        }

        /// <summary>JSON 객체를 T로 파싱한다. 실패하면 null.</summary>
        public static T FromJson<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception) { return null; } // HTML 에러 페이지, 일반 텍스트 응답 등
        }

        /// <summary>최상위가 배열인 JSON을 List&lt;T&gt;로 파싱한다. 실패하면 null, 빈 배열이면 빈 리스트.</summary>
        public static List<T> FromJsonArray<T>(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>("{\"items\":" + json + "}");
                return wrapper?.items ?? new List<T>();
            }
            catch (Exception) { return null; }
        }

        /// <summary>객체를 JSON 문자열로 만든다. null이면 null.</summary>
        public static string ToJson(object obj)
        {
            return obj == null ? null : JsonUtility.ToJson(obj);
        }
    }
}
