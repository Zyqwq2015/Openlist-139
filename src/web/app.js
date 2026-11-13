(function () {
  "use strict";

  var $ = function (id) { return document.getElementById(id); };

  var KEYS = {
    url: "ol139.url",
    mount: "ol139.mount",
    type: "ol139.type",
    root: "ol139.root",
    cloud: "ol139.cloud",
    link: "ol139.link",
    remark: "ol139.remark",
    client: "ol139.client",
    user: "ol139.user",
    ignore: "ol139.ignoreCookies"
  };

  var el = {
    browserPill: $("browserPill"),
    quitBtn: $("quitBtn"),
    loginPill: $("loginPill"),
    loginBtn: $("loginBtn"),
    mailBtn: $("mailBtn"),
    refreshBtn: $("refreshBtn"),
    capAuth: $("capAuth"),
    capMail: $("capMail"),
    capAll: $("capAll"),
    capUd: $("capUd"),
    browserHint: $("browserHint"),
    olPill: $("olPill"),
    olUrl: $("olUrl"),
    olToken: $("olToken"),
    toggleToken: $("toggleToken"),
    olUser: $("olUser"),
    olPass: $("olPass"),
    olOtp: $("olOtp"),
    loginTokenBtn: $("loginTokenBtn"),
    testBtn: $("testBtn"),
    ignoreCert: $("ignoreCert"),
    mountPath: $("mountPath"),
    storageType: $("storageType"),
    rootFolderId: $("rootFolderId"),
    useCurrentFolder: $("useCurrentFolder"),
    cloudIdField: $("cloudIdField"),
    cloudId: $("cloudId"),
    linkIdField: $("linkIdField"),
    linkId: $("linkId"),
    clientType: $("clientType"),
    remark: $("remark"),
    copyAllBtn: $("copyAllBtn"),
    authValue: $("authValue"),
    mailValue: $("mailValue"),
    udId: $("udId"),
    allCookieValue: $("allCookieValue"),
    importBtn: $("importBtn"),
    result: $("result")
  };

  var capture = {};
  var polling = false;

  /* ------------------------------------------------------------- utilities */

  function setPill(node, text, cls) {
    node.className = "pill " + (cls || "idle");
    node.textContent = text;
  }

  function toast(message, type) {
    var host = document.querySelector(".toast-host");
    if (!host) {
      host = document.createElement("div");
      host.className = "toast-host";
      document.body.appendChild(host);
    }
    var node = document.createElement("div");
    node.className = "toast " + (type || "");
    node.textContent = message;
    host.appendChild(node);
    setTimeout(function () {
      node.style.transition = "opacity .25s ease, transform .25s ease";
      node.style.opacity = "0";
      node.style.transform = "translateY(6px)";
      setTimeout(function () { node.remove(); }, 260);
    }, 3200);
  }

  function save(key, value) {
    try { localStorage.setItem(key, value); } catch (e) { }
  }

  function restore(key, fallback) {
    try {
      var v = localStorage.getItem(key);
      return v === null ? fallback : v;
    } catch (e) { return fallback; }
  }

  function withLoading(button, promise) {
    if (button.disabled) return Promise.resolve();
    button.disabled = true;
    button.classList.add("loading");
    return Promise.resolve(promise).finally(function () {
      button.disabled = false;
      button.classList.remove("loading");
    });
  }

  async function post(path, body) {
    var res = await fetch(path, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body || {})
    });
    var text = await res.text();
    try {
      return JSON.parse(text);
    } catch (e) {
      return { ok: false, message: "本地服务返回异常：" + text.slice(0, 200) };
    }
  }

  function copyText(text) {
    if (!text) { toast("没有可复制的内容", "bad"); return; }
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(text).then(function () {
        toast("已复制到剪贴板", "ok");
      }, function () {
        fallbackCopy(text);
      });
    } else {
      fallbackCopy(text);
    }
  }

  function fallbackCopy(text) {
    var ta = document.createElement("textarea");
    ta.value = text;
    ta.style.position = "fixed";
    ta.style.left = "-9999px";
    document.body.appendChild(ta);
    ta.select();
    try {
      document.execCommand("copy");
      toast("已复制到剪贴板", "ok");
    } catch (e) {
      toast("复制失败，请手动选择复制", "bad");
    }
    ta.remove();
  }

  function cookieCount(value) {
    if (!value) return 0;
    return value.split(";").filter(function (s) { return s.trim().length > 0; }).length;
  }

  /* ---------------------------------------------------------------- restore */

  el.olUrl.value = restore(KEYS.url, "");
  el.mountPath.value = restore(KEYS.mount, "/139");
  el.storageType.value = restore(KEYS.type, "personal_new");
  el.rootFolderId.value = restore(KEYS.root, "");
  el.cloudId.value = restore(KEYS.cloud, "");
  el.linkId.value = restore(KEYS.link, "");
  el.remark.value = restore(KEYS.remark, "中国移动云盘");
  el.clientType.value = restore(KEYS.client, "web");
  el.olUser.value = restore(KEYS.user, "");
  el.ignoreCert.checked = restore(KEYS.ignore, "0") === "1";

  el.olUrl.addEventListener("input", function () { save(KEYS.url, el.olUrl.value); });
  el.mountPath.addEventListener("input", function () { save(KEYS.mount, el.mountPath.value); });
  el.storageType.addEventListener("change", function () {
    save(KEYS.type, el.storageType.value);
    syncTypeFields();
  });
  el.rootFolderId.addEventListener("input", function () { save(KEYS.root, el.rootFolderId.value); });
  el.cloudId.addEventListener("input", function () { save(KEYS.cloud, el.cloudId.value); });
  el.linkId.addEventListener("input", function () { save(KEYS.link, el.linkId.value); });
  el.remark.addEventListener("input", function () { save(KEYS.remark, el.remark.value); });
  el.clientType.addEventListener("change", function () { save(KEYS.client, el.clientType.value); });
  el.olUser.addEventListener("input", function () { save(KEYS.user, el.olUser.value); });
  el.ignoreCert.addEventListener("change", function () {
    save(KEYS.ignore, el.ignoreCert.checked ? "1" : "0");
  });

  el.authValue.addEventListener("input", function () { el.authValue.dataset.auto = "0"; });
  el.mailValue.addEventListener("input", function () { el.mailValue.dataset.auto = "0"; });
  el.udId.addEventListener("input", function () { el.udId.dataset.auto = "0"; });

  function syncTypeFields() {
    var type = el.storageType.value;
    el.cloudIdField.hidden = !(type === "family" || type === "group");
    el.linkIdField.hidden = type !== "share";
  }
  syncTypeFields();

  /* ------------------------------------------------------------------ state */

  function renderState(state) {
    if (!state) return;
    var b = state.browser || {};
    var c = state.capture || {};
    capture = c;

    if (b.connected) setPill(el.browserPill, "浏览器已连接", "ok");
    else if (b.running) setPill(el.browserPill, "浏览器启动中", "warn");
    else if (b.error) setPill(el.browserPill, "浏览器异常", "bad");
    else setPill(el.browserPill, "浏览器未启动", "idle");

    if (!b.available) {
      el.browserHint.textContent = "未找到 Chrome / Edge。请先安装 Chrome，或手动把 Authorization / Cookie 粘贴到第 4 步后导入。";
    } else {
      el.browserHint.textContent = b.error || "";
    }

    setPill(el.loginPill, c.loggedIn ? "已登录" : "等待登录", c.loggedIn ? "ok" : "idle");

    el.capAuth.textContent = c.hasAuth ? "已捕获" : "未捕获";
    el.capAuth.className = c.hasAuth ? "cap-ok" : "";

    el.capMail.textContent = c.mailCookies ? (cookieCount(c.mailCookies) + " 项") : "未捕获";
    el.capMail.className = c.mailCookies ? "cap-ok" : "";

    el.capAll.textContent = (c.cookieCount || 0) + " 项";
    el.capAll.className = (c.cookieCount || 0) > 0 ? "cap-ok" : "";

    el.capUd.textContent = c.userDomainId ? "已捕获" : "未捕获";
    el.capUd.className = c.userDomainId ? "cap-ok" : "";

    autofill(el.authValue, c.authorization);
    autofill(el.mailValue, c.mailCookies);
    autofill(el.udId, c.userDomainId);
    if (c.allCookies) el.allCookieValue.value = c.allCookies;
    if (c.rootFolderId) el.useCurrentFolder.title = "使用当前文件夹 ID：" + c.rootFolderId;

    el.loginBtn.textContent = c.loggedIn ? "重新打开登录窗口" : "打开登录窗口";
  }

  function autofill(input, value) {
    if (!value) return;
    var isAuto = input.dataset.auto === "1";
    if (!input.value || isAuto) {
      if (input.value !== value) {
        input.value = value;
        input.dataset.auto = "1";
      }
    }
  }

  async function pollState() {
    if (polling) return;
    polling = true;
    try {
      var res = await fetch("/api/state", { cache: "no-store" });
      var state = await res.json();
      renderState(state);
    } catch (e) {
      setPill(el.browserPill, "本地服务未响应", "bad");
    } finally {
      polling = false;
    }
  }

  /* ---------------------------------------------------------------- actions */

  el.loginBtn.addEventListener("click", function () {
    withLoading(el.loginBtn, post("/api/browser/open-login", {}).then(function (res) {
      if (res.ok) {
        toast("已打开登录窗口，请在新窗口中登录", "ok");
      } else {
        toast(res.message || "打开登录窗口失败", "bad");
      }
      pollState();
    }));
  });

  el.mailBtn.addEventListener("click", function () {
    withLoading(el.mailBtn, post("/api/browser/open-url", { url: "https://mail.10086.cn/" }).then(function (res) {
      if (res.ok) toast("已打开 139 邮箱登录页，登录后可抓取邮箱 Cookie", "ok");
      else toast(res.message || "打开失败", "bad");
      setTimeout(pollState, 800);
    }));
  });

  el.refreshBtn.addEventListener("click", function () {
    withLoading(el.refreshBtn, post("/api/browser/refresh", {}).then(function (res) {
      if (res.ok) toast("已刷新抓取结果", "ok");
      pollState();
    }));
  });

  el.toggleToken.addEventListener("click", function () {
    var showing = el.olToken.type === "text";
    el.olToken.type = showing ? "password" : "text";
    el.toggleToken.textContent = showing ? "显示" : "隐藏";
  });

  el.loginTokenBtn.addEventListener("click", function () {
    var url = el.olUrl.value.trim();
    if (!url) { toast("请先填写 OpenList 地址", "bad"); el.olUrl.focus(); return; }
    if (!el.olUser.value.trim()) { toast("请填写管理员用户名", "bad"); el.olUser.focus(); return; }
    withLoading(el.loginTokenBtn, post("/api/openlist/login", {
      url: url,
      username: el.olUser.value.trim(),
      password: el.olPass.value,
      otp: el.olOtp.value.trim(),
      ignoreCert: el.ignoreCert.checked
    }).then(function (res) {
      if (res.ok && res.token) {
        el.olToken.value = res.token;
        toast("已获取 Token", "ok");
      } else {
        toast(res.message || "获取 Token 失败", "bad");
        if (res.needOtp) el.olOtp.focus();
      }
    }));
  });

  el.testBtn.addEventListener("click", function () {
    var url = el.olUrl.value.trim();
    if (!url) { toast("请填写 OpenList 地址", "bad"); el.olUrl.focus(); return; }
    if (!el.olToken.value.trim()) { toast("请填写管理员 Token，或先获取 Token", "bad"); el.olToken.focus(); return; }
    withLoading(el.testBtn, post("/api/openlist/test", {
      url: url,
      token: el.olToken.value.trim(),
      ignoreCert: el.ignoreCert.checked
    }).then(function (res) {
      setPill(el.olPill, res.ok ? "已连接" : "连接失败", res.ok ? "ok" : "bad");
      toast(res.message || (res.ok ? "连接成功" : "连接失败"), res.ok ? "ok" : "bad");
    }));
  });

  el.useCurrentFolder.addEventListener("click", function () {
    if (!capture.rootFolderId) {
      toast("还没有抓到当前文件夹 ID，请在云盘网页进入目标文件夹后再试", "bad");
      return;
    }
    el.rootFolderId.value = capture.rootFolderId;
    save(KEYS.root, el.rootFolderId.value);
    toast("已填入当前文件夹 ID", "ok");
  });

  el.copyAllBtn.addEventListener("click", function () {
    var text =
      "Authorization: " + el.authValue.value.trim() + "\n" +
      "MailCookies: " + el.mailValue.value.trim() + "\n" +
      "UserDomainID: " + el.udId.value.trim() + "\n";
    copyText(text);
  });

  el.importBtn.addEventListener("click", function () {
    var payload = {
      url: el.olUrl.value.trim(),
      token: el.olToken.value.trim(),
      ignoreCert: el.ignoreCert.checked,
      mountPath: el.mountPath.value.trim(),
      type: el.storageType.value,
      rootFolderId: el.rootFolderId.value.trim(),
      cloudId: el.cloudId.value.trim(),
      linkId: el.linkId.value.trim(),
      clientType: el.clientType.value,
      remark: el.remark.value.trim(),
      authorization: el.authValue.value.trim(),
      mailCookies: el.mailValue.value.trim(),
      userDomainId: el.udId.value.trim()
    };

    if (!payload.url) { toast("请填写 OpenList 地址", "bad"); el.olUrl.focus(); return; }
    if (!payload.token) { toast("请填写管理员 Token", "bad"); el.olToken.focus(); return; }
    if (!payload.mountPath) { toast("请填写挂载路径", "bad"); el.mountPath.focus(); return; }
    if (!payload.authorization && !payload.mailCookies) {
      toast("请先登录并抓取授权，或在第 4 步手动粘贴", "bad");
      el.loginBtn.focus();
      return;
    }

    withLoading(el.importBtn, post("/api/import", payload).then(function (res) {
      showResult(res);
    }).catch(function (err) {
      showResult({ ok: false, message: String(err) });
    }));
  });

  el.quitBtn.addEventListener("click", function () {
    if (!confirm("确定退出 Openlist-139 吗？退出后会关闭本工具启动的浏览器。")) return;
    post("/api/quit", {}).finally(function () {
      try { window.close(); } catch (e) { }
      setTimeout(function () {
        document.body.innerHTML = '<div style="display:grid;place-items:center;height:100vh;color:#8fa0b5;font-family:Segoe UI,Microsoft YaHei UI,sans-serif">Openlist-139 已退出，可以关闭此窗口。</div>';
      }, 500);
    });
  });

  function showResult(res) {
    el.result.classList.remove("hidden");
    el.result.className = "result " + (res.ok ? "ok" : "bad");

    while (el.result.firstChild) el.result.removeChild(el.result.firstChild);

    var title = document.createElement("h3");
    title.textContent = res.ok ? "导入成功" : "导入未完成";
    el.result.appendChild(title);

    var msg = document.createElement("div");
    msg.className = "result-msg";
    msg.textContent = res.message || (res.ok ? "已成功创建存储" : "请检查填写内容后重试");
    el.result.appendChild(msg);

    var meta = document.createElement("div");
    meta.className = "result-meta";
    if (res.id) meta.appendChild(chip("存储 #" + res.id));
    if (res.status) meta.appendChild(chip("状态：" + res.status));
    if (res.mountPath) meta.appendChild(chip("挂载：" + res.mountPath));
    if (meta.childNodes.length) el.result.appendChild(meta);

    if (res.logs && res.logs.length) {
      var ul = document.createElement("ul");
      res.logs.forEach(function (line) {
        var li = document.createElement("li");
        li.textContent = line;
        ul.appendChild(li);
      });
      el.result.appendChild(ul);
    }

    if (res.ok) toast("导入成功", "ok");
    else toast(res.message || "导入未完成", "bad");

    el.result.scrollIntoView({ behavior: "smooth", block: "center" });
  }

  function chip(text) {
    var s = document.createElement("span");
    s.className = "chip";
    s.textContent = text;
    return s;
  }

  pollState();
  setInterval(pollState, 1500);
})();
