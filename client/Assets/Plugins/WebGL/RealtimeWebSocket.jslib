// RealtimeWebSocket.jslib
// WebGL 빌드에서 브라우저의 WebSocket을 C#(BrowserWebSocketTransport, W-3-2)이 쓸 수 있게 이어 주는 JavaScript.
//
// 왜 필요한가
//  - WebGL에는 스레드가 없고 .NET ClientWebSocket도 동작하지 않는다. 대신 브라우저에 내장된 WebSocket을 써야 한다.
//  - C#은 브라우저 API를 직접 부를 수 없다. 그래서 이 파일의 함수를 [DllImport("__Internal")]로 불러 쓰고,
//    브라우저 이벤트(onopen·onmessage·onclose)는 C#이 미리 등록한 콜백 함수로 돌려준다.
//
// C#과의 약속 (ClientWebSocketTransport와 같은 동작)
//  1. RealtimeWS_Init으로 콜백 3개를 한 번 등록한다.
//  2. RealtimeWS_Connect는 연결마다 번호(id, 1 이상)를 돌려준다. 실패해도 예외를 던지지 않고 번호를 준다.
//  3. 번호 하나에 대해 onOpen은 0~1번, onMessage는 0번 이상, onClose는 반드시 정확히 1번, 맨 마지막에 온다.
//  4. onClose는 C#이 부른 함수(Connect·Close·Abort) 안에서 바로 불리지 않는다. 항상 나중(브라우저 이벤트·타이머)에 온다.
//  5. onClose의 code가 0이면 "서버가 준 종료 코드 없음"(시간 초과·취소·중단 등)이라는 뜻이다. C#에서 null로 바꾼다.
//  6. 토큰이 든 주소(url)는 절대 로그·이유 문자열에 넣지 않는다.
//
// 메모리
//  - C# → JS 문자열: C#이 넘긴 포인터(메모리 위치)를 UTF8ToString으로 읽는다. JS가 해제하지 않는다.
//  - JS → C# 문자열: _malloc으로 Unity 메모리에 UTF-8로 복사해 포인터를 넘기고, 콜백이 끝나면 _free로 해제한다.
//    C# 콜백은 받은 문자열을 바로 C# string으로 복사하므로 해제해도 안전하다.

var RealtimeWebSocketLib = {

  // ── 공유 상태 ───────────────────────────────────────────────
  // '$'로 시작하는 이름은 C#에서 부를 수 있는 함수가 아니라, 아래 함수들이 함께 쓰는 JS 객체다.
  // (빌드하면 '$'가 빠지고 RealtimeWS라는 이름이 된다)
  $RealtimeWS: {
    instances: {},        // id → 연결 정보 { socket, opened, finished, ... }
    nextId: 1,
    onOpen: 0,            // C# 콜백 함수 포인터 (Init 전에는 0)
    onMessage: 0,
    onClose: 0,
    closeTimeoutMs: 2000, // Close 후 서버 답을 기다리는 최대 시간 (ClientWebSocketTransport와 같다)

    // JS 문자열을 Unity 메모리에 UTF-8로 복사하고 포인터를 돌려준다. 다 쓰면 _free 해야 한다.
    allocString: function (text) {
      var size = lengthBytesUTF8(text) + 1; // +1: 문자열 끝을 알리는 0 바이트
      var ptr = _malloc(size);
      stringToUTF8(text, ptr, size);
      return ptr;
    },

    // C# 콜백이 예외를 던져도 브라우저 이벤트 처리가 멈추지 않게 감싼다.
    safeCall: function (name, fn) {
      try {
        fn();
      } catch (e) {
        console.error('[RealtimeWS] ' + name + ' callback failed: ' + e);
      }
    },

    emitOpen: function (inst) {
      if (!RealtimeWS.onOpen) return;
      RealtimeWS.safeCall('onOpen', function () {
        {{{ makeDynCall('vi', 'RealtimeWS.onOpen') }}}(inst.id);
      });
    },

    emitMessage: function (inst, text) {
      if (!RealtimeWS.onMessage) return;
      var ptr = RealtimeWS.allocString(text);
      try {
        RealtimeWS.safeCall('onMessage', function () {
          {{{ makeDynCall('vii', 'RealtimeWS.onMessage') }}}(inst.id, ptr);
        });
      } finally {
        _free(ptr);
      }
    },

    emitClose: function (id, code, reason) {
      if (!RealtimeWS.onClose) return;
      var ptr = RealtimeWS.allocString(reason || '');
      try {
        RealtimeWS.safeCall('onClose', function () {
          {{{ makeDynCall('viii', 'RealtimeWS.onClose') }}}(id, code, ptr);
        });
      } finally {
        _free(ptr);
      }
    },

    // 연결을 끝낸다. 여러 번 불려도 처음 한 번만 처리한다(약속 3: onClose 정확히 1번).
    //  - 소켓 이벤트를 모두 떼어 내서 이후에 오는 메시지·이벤트는 무시한다.
    //  - 아직 열려 있거나 연결 중이면 소켓을 닫는다.
    //  - onClose는 setTimeout(0)으로 미뤄 보낸다(약속 4). 그래서 C#이 Close·Abort를 부른 도중에
    //    C# 콜백이 끼어드는(재진입) 일이 없다.
    finish: function (inst, code, reason) {
      if (inst.finished) return;
      inst.finished = true;
      inst.opened = false;

      if (inst.connectTimer) { clearTimeout(inst.connectTimer); inst.connectTimer = 0; }
      if (inst.closeTimer) { clearTimeout(inst.closeTimer); inst.closeTimer = 0; }

      var ws = inst.socket;
      inst.socket = null;
      if (ws) {
        ws.onopen = null;
        ws.onmessage = null;
        ws.onerror = null;
        ws.onclose = null;
        // CONNECTING(0)·OPEN(1)이면 닫는다. CLOSING(2)·CLOSED(3)이면 이미 닫히는 중이다.
        if (ws.readyState === 0 || ws.readyState === 1) {
          try { ws.close(1000, 'client disconnect'); } catch (e) { }
        }
      }

      delete RealtimeWS.instances[inst.id];
      setTimeout(function () {
        RealtimeWS.emitClose(inst.id, code, reason);
      }, 0);
    }
  },

  // ── C#에서 부르는 함수 ─────────────────────────────────────

  // 콜백 3개를 등록한다. C#의 [MonoPInvokeCallback] static 메서드 포인터가 넘어온다.
  //  onOpen(int id) / onMessage(int id, string text) / onClose(int id, int code, string reason)
  RealtimeWS_Init: function (onOpen, onMessage, onClose) {
    RealtimeWS.onOpen = onOpen;
    RealtimeWS.onMessage = onMessage;
    RealtimeWS.onClose = onClose;
  },

  // 연결을 시작하고 번호를 돌려준다. 결과는 콜백으로 온다. Init 전이면 0(연결 안 함).
  //  url: ws://… 또는 wss://… (?token=… 포함. 브라우저 WebSocket은 헤더를 붙일 수 없어서 토큰을 주소에 넣는다)
  //  connectTimeoutMs: 이 시간 안에 열리지 않으면 "connect timeout"으로 끝낸다.
  //  maxMessageBytes: 받은 메시지가 이 바이트 수를 넘으면 "message too large"로 끊는다.
  RealtimeWS_Connect: function (urlPtr, connectTimeoutMs, maxMessageBytes) {
    if (!RealtimeWS.onClose) {
      console.error('[RealtimeWS] RealtimeWS_Init must be called before Connect');
      return 0;
    }

    var url = UTF8ToString(urlPtr);
    var inst = {
      id: RealtimeWS.nextId++,
      socket: null,
      opened: false,
      finished: false,
      connectTimer: 0,
      closeTimer: 0,
      maxBytes: maxMessageBytes > 0 ? maxMessageBytes : 65536
    };
    RealtimeWS.instances[inst.id] = inst;

    var ws;
    try {
      ws = new WebSocket(url);
    } catch (e) {
      // 잘못된 주소면 생성자가 SyntaxError를 던진다. 브라우저 오류 메시지에 주소(=토큰)가 들어가므로
      // 메시지는 쓰지 않고 오류 이름만 남긴다(약속 6).
      RealtimeWS.finish(inst, 0, 'connect setup failed: ' + (e && e.name ? e.name : 'Error'));
      return inst.id;
    }
    inst.socket = ws;
    ws.binaryType = 'arraybuffer'; // 바이너리가 와도 Blob(비동기로 읽어야 함) 대신 바로 버릴 수 있게

    inst.connectTimer = setTimeout(function () {
      inst.connectTimer = 0;
      if (!inst.opened) {
        RealtimeWS.finish(inst, 0, 'connect timeout');
      }
    }, connectTimeoutMs > 0 ? connectTimeoutMs : 1);

    ws.onopen = function () {
      if (inst.finished) return;
      if (inst.connectTimer) { clearTimeout(inst.connectTimer); inst.connectTimer = 0; }
      inst.opened = true;
      RealtimeWS.emitOpen(inst);
    };

    ws.onmessage = function (event) {
      if (inst.finished) return;
      var data = event.data;
      if (typeof data !== 'string') {
        return; // 바이너리는 버린다 (서버는 텍스트만 보낸다. ClientWebSocketTransport와 같다)
      }
      if (lengthBytesUTF8(data) > inst.maxBytes) {
        RealtimeWS.finish(inst, 0, 'message too large');
        return;
      }
      RealtimeWS.emitMessage(inst, data);
    };

    // onerror는 브라우저가 이유를 알려 주지 않는다(보안상). 곧이어 onclose가 오므로 여기서는 아무것도 하지 않는다.
    ws.onerror = function () { };

    ws.onclose = function (event) {
      // 핸드셰이크 실패(서버 꺼짐·401 등)는 브라우저가 구분해 주지 않고 모두 1006(비정상 종료)로 온다.
      // 그래서 서버는 인증 실패를 401이 아니라 "연결 후 4401로 닫기"로 알린다.
      var reason = event.reason;
      if (!reason) {
        reason = inst.opened ? 'connection closed' : 'connect failed';
      }
      RealtimeWS.finish(inst, event.code || 0, reason);
    };

    return inst.id;
  },

  // 텍스트 하나를 보낸다. 보냈으면 1, 열려 있지 않아 버렸으면 0.
  // 브라우저 WebSocket.send는 바로 돌아오고, 부른 순서대로 보낸다(브라우저가 내부 큐로 처리).
  RealtimeWS_Send: function (id, textPtr) {
    var inst = RealtimeWS.instances[id];
    if (!inst || !inst.opened || inst.finished || inst.closeTimer || !inst.socket || inst.socket.readyState !== 1) {
      return 0;
    }
    try {
      inst.socket.send(UTF8ToString(textPtr));
      return 1;
    } catch (e) {
      return 0;
    }
  },

  // 정상 종료. 열리기 전이면 연결 시도를 멈추고("cancelled"), 열려 있으면 종료 프레임(1000)을 보낸 뒤
  // 서버 답(onclose)을 최대 2초 기다린다. 답이 없으면 "close timeout"으로 끝낸다.
  RealtimeWS_Close: function (id) {
    var inst = RealtimeWS.instances[id];
    if (!inst || inst.finished || inst.closeTimer) return;

    if (!inst.opened) {
      RealtimeWS.finish(inst, 0, 'cancelled');
      return;
    }
    try {
      inst.socket.close(1000, 'client disconnect');
    } catch (e) {
      RealtimeWS.finish(inst, 0, 'cancelled');
      return;
    }
    inst.closeTimer = setTimeout(function () {
      inst.closeTimer = 0;
      RealtimeWS.finish(inst, 0, 'close timeout');
    }, RealtimeWS.closeTimeoutMs);
  },

  // 즉시 중단. 서버 답을 기다리지 않는다. onClose("aborted")는 나중에 한 번 온다.
  RealtimeWS_Abort: function (id) {
    var inst = RealtimeWS.instances[id];
    if (!inst) return;
    RealtimeWS.finish(inst, 0, 'aborted');
  }
};

// 위 함수들이 모두 $RealtimeWS를 쓴다고 알려 준다. 이게 없으면 빌드 때 RealtimeWS 객체가 빠질 수 있다.
autoAddDeps(RealtimeWebSocketLib, '$RealtimeWS');
// Unity(Emscripten)가 가진 함수 목록(LibraryManager.library)에 우리 함수를 합친다.
mergeInto(LibraryManager.library, RealtimeWebSocketLib);
