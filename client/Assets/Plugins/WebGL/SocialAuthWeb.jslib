// SocialAuthWeb.jslib
// WebGL 빌드에서 브라우저 팝업으로 구글/카카오 로그인을 하고, 받은 "인가 코드"를 C#(WebGLSocialAuthProvider)로 돌려준다.
// 코드를 구글/카카오 토큰으로 바꾸는 일은 서버가 한다. (POST /api/members/login/{google|kakao}/code)
//
// 결과는 항상 JSON 문자열 한 개를 receiver 오브젝트의 OnWebAuthResult 메서드로 보낸다.
//   성공: {"ok":true,"code":"...","redirectUri":"..."}   (redirectUri는 카카오만)
//   실패: {"ok":false,"error":"..."}                     (취소·팝업 차단·설정 오류 등)
//
// 주의
//  - 반드시 사용자의 버튼 클릭 직후에 불러야 한다. 시작하자마자 팝업을 열면 브라우저가 차단한다.
//  - 팝업 차단은 브라우저마다 다르다. Safari/모바일에서 막히면 "팝업이 차단되었습니다" 오류가 온다.
//  - 한 번에 하나의 로그인만 진행한다. 새로 시작하면 이전 대기는 정리한다.

var SocialAuthWebLib = {

  $SocialAuthWeb: {
    cleanup: null, // 진행 중인 로그인의 정리 함수 (리스너·타이머 제거)

    // C#에 결과(JSON)를 보낸다. 정리는 한 번만 한다.
    send: function (receiver, payload) {
      if (SocialAuthWeb.cleanup) { SocialAuthWeb.cleanup(); SocialAuthWeb.cleanup = null; }
      var json = JSON.stringify(payload);
      if (typeof SendMessage === 'function') {
        SendMessage(receiver, 'OnWebAuthResult', json);
      } else if (typeof Module !== 'undefined' && Module.SendMessage) {
        Module.SendMessage(receiver, 'OnWebAuthResult', json);
      } else if (typeof unityInstance !== 'undefined' && unityInstance.SendMessage) {
        unityInstance.SendMessage(receiver, 'OnWebAuthResult', json);
      } else {
        console.error('[SocialAuthWeb] Unity SendMessage를 찾을 수 없습니다.');
      }
    },

    fail: function (receiver, message) {
      SocialAuthWeb.send(receiver, { ok: false, error: message });
    },

    // 이전 로그인이 남아 있으면 정리한다.
    reset: function () {
      if (SocialAuthWeb.cleanup) { SocialAuthWeb.cleanup(); SocialAuthWeb.cleanup = null; }
    },

    // 구글 GIS 스크립트를 한 번만 불러온다. (index.html을 고치지 않아도 되게 하려고 직접 넣는다)
    loadGis: function (onReady, onError) {
      if (typeof google !== 'undefined' && google.accounts && google.accounts.oauth2) { onReady(); return; }
      var existing = document.getElementById('gis-client-script');
      var script = existing || document.createElement('script');
      script.addEventListener('load', onReady);
      script.addEventListener('error', function () { onError('구글 로그인 스크립트를 불러오지 못했습니다.'); });
      if (!existing) {
        script.id = 'gis-client-script';
        script.src = 'https://accounts.google.com/gsi/client';
        script.async = true;
        document.head.appendChild(script);
      }
    },

    randomState: function () {
      var bytes = new Uint8Array(16);
      (window.crypto || window.msCrypto).getRandomValues(bytes);
      var s = '';
      for (var i = 0; i < bytes.length; i++) { s += ('0' + bytes[i].toString(16)).slice(-2); }
      return s;
    }
  },

  // 구글: GIS 팝업(initCodeClient)으로 인가 코드를 받는다. 서버는 redirect_uri="postmessage"로 교환한다.
  SocialAuthWeb_RequestGoogleCode: function (clientIdPtr, receiverPtr) {
    var clientId = UTF8ToString(clientIdPtr);
    var receiver = UTF8ToString(receiverPtr);
    SocialAuthWeb.reset();

    SocialAuthWeb.loadGis(function () {
      try {
        var client = google.accounts.oauth2.initCodeClient({
          client_id: clientId,
          scope: 'openid email profile',
          ux_mode: 'popup',
          callback: function (response) {
            if (response && response.code) {
              SocialAuthWeb.send(receiver, { ok: true, code: response.code });
            } else {
              SocialAuthWeb.fail(receiver, '구글 로그인이 취소되었거나 실패했습니다.');
            }
          },
          // 팝업이 막히거나 사용자가 창을 닫은 경우
          error_callback: function (err) {
            var type = err && err.type ? err.type : 'unknown';
            SocialAuthWeb.fail(receiver, type === 'popup_failed_to_open'
              ? '팝업이 차단되었습니다. 브라우저에서 팝업을 허용해 주세요.'
              : '구글 로그인이 취소되었습니다.');
          }
        });
        client.requestCode();
      } catch (e) {
        SocialAuthWeb.fail(receiver, '구글 로그인을 시작하지 못했습니다: ' + e);
      }
    }, function (message) {
      SocialAuthWeb.fail(receiver, message);
    });
  },

  // 카카오: 팝업(window.open)으로 인가 코드를 받는다.
  // JS SDK의 Kakao.Auth.authorize는 현재 창을 이동시켜 유니티가 다시 로드되므로 쓰지 않는다.
  // redirectUri는 게임 서버의 고정 주소(예: https://서버/kakao-callback.html)다. itch.io처럼 게임 주소가
  // 업로드마다 바뀌는 곳에서도 카카오 콘솔·서버 설정을 다시 고치지 않아도 되게 하려고 게임과 다른 출처에 둔다.
  // 팝업이 그 페이지로 돌아오면 페이지가 postMessage로 게임 창에 코드를 보낸다.
  SocialAuthWeb_RequestKakaoCode: function (restApiKeyPtr, redirectUriPtr, receiverPtr) {
    var restApiKey = UTF8ToString(restApiKeyPtr);
    var redirectUri = UTF8ToString(redirectUriPtr);
    var receiver = UTF8ToString(receiverPtr);
    SocialAuthWeb.reset();

    // 콜백 페이지가 메시지를 보낼 출처. 이 출처에서 온 메시지만 받는다.
    var callbackOrigin;
    try { callbackOrigin = new URL(redirectUri).origin; } catch (e) {
      SocialAuthWeb.fail(receiver, '카카오 Redirect 주소가 올바르지 않습니다.');
      return;
    }

    // CSRF 방지 + 게임 출처 전달: state = 랜덤 + "." + base64url(게임 출처).
    // 콜백 페이지는 state 뒤쪽의 게임 출처(허용 목록에 있을 때만)로 코드를 보내고, 게임은 같은 state일 때만 받는다.
    var gameOrigin = btoa(window.location.origin).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
    var state = SocialAuthWeb.randomState() + '.' + gameOrigin;

    var popup = null;
    var timer = null;

    var onMessage = function (event) {
      if (event.origin !== callbackOrigin) return;
      var data = event.data;
      if (!data || data.type !== 'KAKAO_AUTH_RESULT') return;
      if (data.state !== state) return; // 다른 로그인 시도·위조된 메시지는 무시
      if (data.code) {
        SocialAuthWeb.send(receiver, { ok: true, code: data.code, redirectUri: redirectUri });
      } else {
        SocialAuthWeb.fail(receiver, data.error === 'access_denied'
          ? '카카오 로그인이 취소되었습니다.'
          : '카카오 로그인에 실패했습니다. (' + (data.error || 'unknown') + ')');
      }
    };
    window.addEventListener('message', onMessage);

    SocialAuthWeb.cleanup = function () {
      window.removeEventListener('message', onMessage);
      if (timer) { clearInterval(timer); timer = null; }
      try { if (popup && !popup.closed) popup.close(); } catch (e) {}
    };

    var url = 'https://kauth.kakao.com/oauth/authorize'
      + '?client_id=' + encodeURIComponent(restApiKey)
      + '&redirect_uri=' + encodeURIComponent(redirectUri)
      + '&response_type=code'
      + '&state=' + encodeURIComponent(state);
    popup = window.open(url, 'kakaoLoginPopup', 'width=480,height=640');
    if (!popup) {
      SocialAuthWeb.fail(receiver, '팝업이 차단되었습니다. 브라우저에서 팝업을 허용해 주세요.');
      return;
    }

    // 결과 없이 사용자가 팝업을 닫은 경우: 결과 메시지가 도착할 시간을 조금 준 뒤 취소로 처리한다.
    timer = setInterval(function () {
      var closed = false;
      try { closed = popup.closed; } catch (e) { closed = false; }
      if (closed) {
        clearInterval(timer);
        timer = null;
        setTimeout(function () {
          if (SocialAuthWeb.cleanup) { SocialAuthWeb.fail(receiver, '카카오 로그인이 취소되었습니다.'); }
        }, 500);
      }
    }, 500);
  }
};

autoAddDeps(SocialAuthWebLib, '$SocialAuthWeb');
mergeInto(LibraryManager.library, SocialAuthWebLib);
