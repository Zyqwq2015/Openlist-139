using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Openlist139
{
    internal static class Program
    {
        private static HttpServer _server;
        private static BrowserManager _browser;
        private static CaptureStore _capture;
        private static string _appDir;
        private static int _port;
        private static string _logPath;
        private static readonly object _logLock = new object();

        [STAThread]
        private static void Main(string[] args)
        {
            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            string dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Openlist-139");
            try { Directory.CreateDirectory(dataDir); } catch { }
            _logPath = Path.Combine(dataDir, "app.log");

            bool noBrowser = false;
            int wantPort = 53139;
            if (args != null)
            {
                foreach (string a in args)
                {
                    if (a == "--no-browser") noBrowser = true;
                    else if (a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
                    {
                        int p;
                        if (int.TryParse(a.Substring(7), out p) && p > 0 && p < 65536) wantPort = p;
                    }
                }
            }

            Log("==== Openlist-139 启动 ====");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _capture = new CaptureStore();
            _browser = new BrowserManager(_capture);

            _port = HttpServer.FindFreePort(wantPort);
            _server = new HttpServer(_port, HandleRequest);
            _server.Start();
            Log("本地面板: http://127.0.0.1:" + _port + "/");
            try { File.WriteAllText(Path.Combine(dataDir, "port.txt"), _port.ToString()); } catch { }

            string home = "http://127.0.0.1:" + _port + "/";
            if (!noBrowser)
            {
                try
                {
                    _browser.Launch(home);
                }
                catch (Exception ex)
                {
                    Log("启动浏览器失败: " + ex.Message);
                    try { Process.Start(new ProcessStartInfo(home) { UseShellExecute = true }); } catch { }
                }
            }

            var wait = new ManualResetEvent(false);
            wait.WaitOne();
        }

        internal static void Log(string message)
        {
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(_logPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- HTTP

        private static HttpResponse HandleRequest(HttpRequest req)
        {
            try
            {
                string path = req.Path;

                if (req.Method == "GET" || req.Method == "HEAD")
                {
                    if (path == "/" || path == "/index.html")
                        return HttpResponse.Asset("web.index.html", "text/html; charset=utf-8", "index.html");
                    if (path == "/app.js")
                        return HttpResponse.Asset("web.app.js", "application/javascript; charset=utf-8", "app.js");
                    if (path == "/style.css")
                        return HttpResponse.Asset("web.style.css", "text/css; charset=utf-8", "style.css");
                    if (path == "/favicon.ico")
                        return new HttpResponse { Status = 204, ContentType = "image/x-icon", Body = new byte[0] };
                }

                if (path.StartsWith("/api/", StringComparison.Ordinal))
                {
                    if (req.Method == "GET" && path == "/api/state")
                        return HttpResponse.JsonObject(BuildState(), 200);

                    if (req.Method != "POST")
                        return HttpResponse.JsonObject(new Dictionary<string, object> { { "ok", false }, { "message", "请使用 POST" } }, 405);

                    Dictionary<string, object> body = req.JsonBody();
                    switch (path)
                    {
                        case "/api/state":
                            return HttpResponse.JsonObject(BuildState(), 200);
                        case "/api/browser/open-login":
                            {
                                try
                                {
                                    _browser.OpenLogin("http://127.0.0.1:" + _port + "/");
                                    return HttpResponse.JsonObject(new Dictionary<string, object> { { "ok", true } }, 200);
                                }
                                catch (Exception ex)
                                {
                                    return HttpResponse.JsonObject(new Dictionary<string, object> { { "ok", false }, { "message", ex.Message } }, 200);
                                }
                            }
                        case "/api/browser/open-url":
                            {
                                try
                                {
                                    _browser.OpenUrl(Util.Str(body, "url"));
                                    return HttpResponse.JsonObject(new Dictionary<string, object> { { "ok", true } }, 200);
                                }
                                catch (Exception ex)
                                {
                                    return HttpResponse.JsonObject(new Dictionary<string, object> { { "ok", false }, { "message", ex.Message } }, 200);
                                }
                            }
                        case "/api/browser/refresh":
                            {
                                try { _browser.RefreshCookies(); } catch { }
                                return HttpResponse.JsonObject(new Dictionary<string, object> { { "ok", true }, { "capture", _capture.Snapshot() } }, 200);
                            }
                        case "/api/openlist/login":
                            return HttpResponse.JsonObject(OpenListLogin(body), 200);
                        case "/api/openlist/test":
                            return HttpResponse.JsonObject(OpenListTest(body), 200);
                        case "/api/import":
                            return HttpResponse.JsonObject(ImportStorage(body), 200);
                        case "/api/quit":
                            {
                                var resp = HttpResponse.JsonObject(new Dictionary<string, object> { { "ok", true } }, 200);
                                Task.Run(delegate
                                {
                                    try { _browser.Close(); } catch { }
                                    Thread.Sleep(250);
                                    Environment.Exit(0);
                                });
                                return resp;
                            }
                    }
                }

                return HttpResponse.Text("Not Found", "text/plain; charset=utf-8", 404);
            }
            catch (Exception ex)
            {
                Log("请求处理错误: " + ex);
                return HttpResponse.JsonObject(new Dictionary<string, object> { { "ok", false }, { "message", ex.Message } }, 500);
            }
        }

        private static Dictionary<string, object> BuildState()
        {
            var state = new Dictionary<string, object>();
            state["app"] = new Dictionary<string, object>
            {
                { "version", "1.0.0" },
                { "port", _port },
                { "startedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
            };
            state["browser"] = _browser.State();
            state["capture"] = _capture.Snapshot();
            return state;
        }

        // --------------------------------------------------------- OpenList API

        private static Dictionary<string, object> OpenListLogin(Dictionary<string, object> body)
        {
            string baseUrl = Util.NormalizeBase(Util.Str(body, "url"));
            string username = Util.Str(body, "username").Trim();
            string password = Util.Str(body, "password");
            string otp = Util.Str(body, "otp").Trim();
            bool ignoreCert = Util.Bool(body, "ignoreCert");

            if (baseUrl.Length == 0) return Error("请填写 OpenList 地址");
            if (username.Length == 0) return Error("请填写 OpenList 管理员用户名");

            var payload = new Dictionary<string, object>
            {
                { "username", username },
                { "password", password },
                { "otp_code", otp }
            };

            HttpResult res = HttpUtil.Call(baseUrl, "/api/auth/login", null, payload, ignoreCert);
            if (res.Json == null)
                return Error(res.ErrorText());

            int code = Util.Int(res.Json, "code");
            if (code == 200)
            {
                var data = Util.Dict(res.Json, "data");
                string token = Util.Str(data, "token");
                if (token.Length == 0) return Error("登录成功但没有返回 Token");
                return new Dictionary<string, object>
                {
                    { "ok", true },
                    { "token", token },
                    { "message", "已获取管理员 Token" }
                };
            }
            if (code == 402)
            {
                return new Dictionary<string, object>
                {
                    { "ok", false },
                    { "needOtp", true },
                    { "message", "该账号开启了两步验证，请填写动态验证码后重试" }
                };
            }
            return Error(Util.Str(res.Json, "message").Length > 0 ? Util.Str(res.Json, "message") : ("登录失败（HTTP " + res.Status + "）"));
        }

        private static Dictionary<string, object> OpenListTest(Dictionary<string, object> body)
        {
            string baseUrl = Util.NormalizeBase(Util.Str(body, "url"));
            string token = Util.Str(body, "token").Trim();
            bool ignoreCert = Util.Bool(body, "ignoreCert");
            if (baseUrl.Length == 0) return Error("请填写 OpenList 地址");
            if (token.Length == 0) return Error("请填写管理员 Token，或先用账号密码获取 Token");

            HttpResult me = HttpUtil.Call(baseUrl, "/api/me", token, null, ignoreCert);
            if (me.Json == null) return Error(me.ErrorText());
            if (Util.Int(me.Json, "code") != 200)
                return Error("Token 无效或已过期：" + Util.Str(me.Json, "message"));

            string name = Util.Str(Util.Dict(me.Json, "data"), "username");
            HttpResult drv = HttpUtil.Call(baseUrl, "/api/admin/driver/info?driver=139Yun", token, null, ignoreCert);
            bool driverOk = drv.Json != null && Util.Int(drv.Json, "code") == 200;

            return new Dictionary<string, object>
            {
                { "ok", driverOk },
                { "driverOk", driverOk },
                { "message", driverOk
                    ? ("连接成功，管理员：" + (name.Length > 0 ? name : "admin") + "，139Yun 驱动可用")
                    : "已连接 OpenList，但未找到 139Yun 驱动，请升级 OpenList（建议 v4.2.4+）" }
            };
        }

        private static Dictionary<string, object> ImportStorage(Dictionary<string, object> body)
        {
            var logs = new List<string>();
            string baseUrl = Util.NormalizeBase(Util.Str(body, "url"));
            string token = Util.Str(body, "token").Trim();
            bool ignoreCert = Util.Bool(body, "ignoreCert");

            string mountPath = Util.NormalizeMount(Util.Str(body, "mountPath"));
            string type = Util.Str(body, "type").Trim();
            if (type.Length == 0) type = "personal_new";
            string rootFolder = Util.Str(body, "rootFolderId").Trim();
            string cloudId = Util.Str(body, "cloudId").Trim();
            string linkId = Util.Str(body, "linkId").Trim();
            string remark = Util.Str(body, "remark").Trim();
            string clientType = Util.Str(body, "clientType").Trim();
            if (clientType.Length == 0) clientType = "web";

            var snap = _capture.Snapshot();
            string authorization = Util.Str(body, "authorization").Trim();
            string mailCookies = Util.Str(body, "mailCookies").Trim();
            string userDomainId = Util.Str(body, "userDomainId").Trim();
            if (authorization.Length == 0) authorization = Util.Str(snap, "authorization").Trim();
            if (mailCookies.Length == 0) mailCookies = Util.Str(snap, "mailCookies").Trim();
            if (userDomainId.Length == 0) userDomainId = Util.Str(snap, "userDomainId").Trim();

            if (baseUrl.Length == 0) return Error("请填写 OpenList 地址");
            if (token.Length == 0) return Error("请填写 OpenList 管理员 Token，或先用账号密码获取 Token");
            if (mountPath.Length == 0) return Error("请填写挂载路径，例如 /139");
            if (authorization.Length == 0 && mailCookies.Length == 0)
                return Error("还没有抓到授权信息。请先登录中国移动云盘，或在“抓取结果”里手动粘贴 Authorization / 邮箱 Cookie");

            if (type == "family" || type == "group")
            {
                if (cloudId.Length == 0) return Error("家庭云 / 共享群类型需要填写 Cloud ID");
            }
            if (type == "share" && linkId.Length == 0)
                return Error("分享挂载类型需要填写分享链接 ID");

            if (type == "personal_new" && rootFolder.Length == 0) rootFolder = "/";
            if (type == "personal" && rootFolder.Length == 0) rootFolder = "root";
            if (type == "group" && rootFolder.Length == 0) rootFolder = cloudId;

            logs.Add("正在连接 OpenList ...");
            HttpResult drv = HttpUtil.Call(baseUrl, "/api/admin/driver/info?driver=139Yun", token, null, ignoreCert);
            if (drv.Json == null) return ErrorWithLogs(drv.ErrorText(), logs);
            if (Util.Int(drv.Json, "code") != 200)
            {
                if (drv.Status == 401)
                    return ErrorWithLogs("管理员 Token 无效或已过期", logs);
                if (drv.Status == 404 || Util.Int(drv.Json, "code") == 404)
                    return ErrorWithLogs("当前 OpenList 没有 139Yun 驱动，请升级到包含 139 驱动的版本（v4.2.4+）", logs);
                return ErrorWithLogs("检查驱动失败：" + Util.Str(drv.Json, "message"), logs);
            }
            logs.Add("139Yun 驱动可用");

            HttpResult list = HttpUtil.Call(baseUrl, "/api/admin/storage/list?page=1&per_page=1000", token, null, ignoreCert);
            if (list.Json == null) return ErrorWithLogs("读取存储列表失败：" + list.ErrorText(), logs);
            if (Util.Int(list.Json, "code") != 200)
                return ErrorWithLogs("读取存储列表失败：" + Util.Str(list.Json, "message"), logs);

            Dictionary<string, object> existing = null;
            object[] items = Util.Array(Util.Dict(list.Json, "data"), "content");
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i] as Dictionary<string, object>;
                if (item == null) continue;
                if (Util.NormalizeMount(Util.Str(item, "mount_path")) == mountPath)
                {
                    existing = item;
                    break;
                }
            }

            var addition = new Dictionary<string, object>
            {
                { "authorization", authorization },
                { "username", "" },
                { "password", "" },
                { "sms_code", "" },
                { "mail_cookies", mailCookies },
                { "root_folder_id", rootFolder },
                { "type", type },
                { "link_id", linkId },
                { "cloud_id", cloudId },
                { "user_domain_id", userDomainId },
                { "custom_upload_part_size", 0 },
                { "report_real_size", true },
                { "use_large_thumbnail", false },
                { "use_old_stream_upload", false },
                { "client_type", clientType }
            };
            string additionJson = Util.Serialize(addition);

            var payload = new Dictionary<string, object>
            {
                { "mount_path", mountPath },
                { "order", 0 },
                { "driver", "139Yun" },
                { "cache_expiration", 30 },
                { "custom_cache_policies", "" },
                { "status", "work" },
                { "addition", additionJson },
                { "remark", remark },
                { "disabled", false },
                { "disable_index", false },
                { "enable_sign", false },
                { "seed_policy", "inherit" },
                { "order_by", "name" },
                { "order_direction", "asc" },
                { "extract_folder", "" },
                { "web_proxy", false },
                { "webdav_policy", "302_redirect" },
                { "proxy_range", true },
                { "down_proxy_url", "" },
                { "disable_proxy_sign", false }
            };

            string apiPath;
            if (existing != null)
            {
                int existingId = Util.Int(existing, "id");
                payload["id"] = existingId;
                CopyIfPresent(existing, payload, "order");
                CopyIfPresent(existing, payload, "cache_expiration");
                CopyIfPresent(existing, payload, "custom_cache_policies");
                CopyIfPresent(existing, payload, "web_proxy");
                CopyIfPresent(existing, payload, "webdav_policy");
                CopyIfPresent(existing, payload, "proxy_range");
                CopyIfPresent(existing, payload, "order_by");
                CopyIfPresent(existing, payload, "order_direction");
                CopyIfPresent(existing, payload, "extract_folder");
                CopyIfPresent(existing, payload, "seed_policy");
                CopyIfPresent(existing, payload, "disable_index");
                CopyIfPresent(existing, payload, "enable_sign");
                if (remark.Length == 0) payload["remark"] = Util.Str(existing, "remark");
                apiPath = "/api/admin/storage/update";
                logs.Add("发现挂载路径 " + mountPath + " 已存在（#" + existingId + "），将更新该存储");
            }
            else
            {
                apiPath = "/api/admin/storage/create";
                logs.Add("将在 " + mountPath + " 创建 139Yun 存储");
            }

            HttpResult save = HttpUtil.Call(baseUrl, apiPath, token, payload, ignoreCert);
            int storageId = 0;
            if (save.Json != null)
            {
                storageId = Util.Int(Util.Dict(save.Json, "data"), "id");
            }

            if (save.Json == null || Util.Int(save.Json, "code") != 200)
            {
                string msg = save.Json != null ? Util.Str(save.Json, "message") : save.ErrorText();
                if (storageId > 0)
                {
                    string st = FetchStorageStatus(baseUrl, token, storageId, ignoreCert);
                    logs.Add("已保存存储 #" + storageId + "，但初始化失败");
                    if (st.Length > 0) logs.Add("存储状态：" + st);
                    return new Dictionary<string, object>
                    {
                        { "ok", false },
                        { "created", true },
                        { "id", storageId },
                        { "status", st },
                        { "message", msg },
                        { "logs", logs },
                        { "additionJson", additionJson }
                    };
                }
                return ErrorWithLogs(msg, logs);
            }

            if (storageId == 0) storageId = FindStorageId(baseUrl, token, mountPath, ignoreCert);
            string status = storageId > 0 ? FetchStorageStatus(baseUrl, token, storageId, ignoreCert) : "";
            logs.Add("OpenList 返回成功");

            if (status.Length > 0 && status != "work")
            {
                return new Dictionary<string, object>
                {
                    { "ok", false },
                    { "created", true },
                    { "id", storageId },
                    { "status", status },
                    { "message", "存储已保存，但初始化失败：" + status },
                    { "logs", logs },
                    { "additionJson", additionJson }
                };
            }

            return new Dictionary<string, object>
            {
                { "ok", true },
                { "id", storageId },
                { "status", status.Length > 0 ? status : "work" },
                { "mountPath", mountPath },
                { "message", "导入成功！已挂载到 " + mountPath + "（存储 #" + storageId + "）" },
                { "logs", logs },
                { "additionJson", additionJson }
            };
        }

        private static void CopyIfPresent(Dictionary<string, object> from, Dictionary<string, object> to, string key)
        {
            object v;
            if (from.TryGetValue(key, out v) && v != null) to[key] = v;
        }

        private static int FindStorageId(string baseUrl, string token, string mountPath, bool ignoreCert)
        {
            HttpResult list = HttpUtil.Call(baseUrl, "/api/admin/storage/list?page=1&per_page=1000", token, null, ignoreCert);
            if (list.Json == null) return 0;
            object[] items = Util.Array(Util.Dict(list.Json, "data"), "content");
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i] as Dictionary<string, object>;
                if (item == null) continue;
                if (Util.NormalizeMount(Util.Str(item, "mount_path")) == mountPath) return Util.Int(item, "id");
            }
            return 0;
        }

        private static string FetchStorageStatus(string baseUrl, string token, int id, bool ignoreCert)
        {
            if (id <= 0) return "";
            HttpResult res = HttpUtil.Call(baseUrl, "/api/admin/storage/get?id=" + id, token, null, ignoreCert);
            if (res.Json == null || Util.Int(res.Json, "code") != 200) return "";
            var data = Util.Dict(res.Json, "data");
            string status = Util.Str(data, "status");
            return status;
        }

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object> { { "ok", false }, { "message", message } };
        }

        private static Dictionary<string, object> ErrorWithLogs(string message, List<string> logs)
        {
            logs.Add(message);
            return new Dictionary<string, object> { { "ok", false }, { "message", message }, { "logs", logs } };
        }
    }

    // ------------------------------------------------------------- 抓取状态

    internal sealed class CaptureStore
    {
        private readonly object _lock = new object();
        private string _authorization = "";
        private string _authorizationFull = "";
        private string _mailCookies = "";
        private string _allCookies = "";
        private string _userDomainId = "";
        private string _rootFolderId = "";
        private int _cookieCount;
        private bool _hasSso;
        private bool _hasCloudToken;
        private DateTime _updated = DateTime.MinValue;

        public void SetAuthorization(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            string full = raw.Trim();
            string value = full;
            if (value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(6).Trim();
            lock (_lock)
            {
                if (_authorizationFull == full) return;
                _authorizationFull = full;
                _authorization = value;
                _updated = DateTime.Now;
                Program.Log("已捕获 Authorization（长度 " + value.Length + "）");
            }
        }

        public void SetRootFolderId(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            id = id.Trim();
            if (id.Length == 0 || id == "/" || id == "0" || id == "root") return;
            lock (_lock)
            {
                if (_rootFolderId == id) return;
                _rootFolderId = id;
                _updated = DateTime.Now;
            }
        }

        public void SetCookies(object[] cookies)
        {
            var mail = new List<string>();
            var all = new List<string>();
            var seenMail = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasSso = false;
            bool hasCloudToken = false;
            string udId = "";
            int count = 0;

            if (cookies != null)
            {
                foreach (object o in cookies)
                {
                    var c = o as Dictionary<string, object>;
                    if (c == null) continue;
                    string name = Util.Str(c, "name");
                    string value = Util.Str(c, "value");
                    string domain = Util.Str(c, "domain").ToLowerInvariant();
                    if (name.Length == 0) continue;

                    bool is10086 = domain == "10086.cn" || domain.EndsWith(".10086.cn", StringComparison.Ordinal);
                    bool is139 = domain == "139.com" || domain.EndsWith(".139.com", StringComparison.Ordinal);
                    if (!is10086 && !is139) continue;

                    count++;
                    string pair = name + "=" + value;
                    if (is10086 && seenMail.Add(name)) mail.Add(pair);
                    if (seenAll.Add(domain + "|" + name)) all.Add(pair);

                    if (name.Equals("Os_Sso_Sid", StringComparison.OrdinalIgnoreCase)) hasSso = true;
                    if (name.Equals("MCloudSessionID", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("MCloudToken", StringComparison.OrdinalIgnoreCase)) hasCloudToken = true;
                    if (name.Equals("ud_id", StringComparison.OrdinalIgnoreCase) && value.Length > 0) udId = value;
                }
            }

            string mailStr = string.Join("; ", mail.ToArray());
            string allStr = string.Join("; ", all.ToArray());

            lock (_lock)
            {
                bool changed = _mailCookies != mailStr || _allCookies != allStr ||
                               _cookieCount != count || _hasSso != hasSso || _hasCloudToken != hasCloudToken ||
                               _userDomainId != udId;
                _mailCookies = mailStr;
                _allCookies = allStr;
                _cookieCount = count;
                _hasSso = hasSso;
                _hasCloudToken = hasCloudToken;
                if (udId.Length > 0) _userDomainId = udId;
                if (changed)
                {
                    _updated = DateTime.Now;
                    Program.Log("已刷新 Cookie：相关 " + count + " 个，邮箱 Cookie " + mail.Count + " 个");
                }
            }
        }

        public Dictionary<string, object> Snapshot()
        {
            lock (_lock)
            {
                bool hasAuth = _authorization.Length > 0;
                bool loggedIn = hasAuth || _hasSso || _hasCloudToken;
                return new Dictionary<string, object>
                {
                    { "authorization", _authorization },
                    { "authorizationFull", _authorizationFull },
                    { "mailCookies", _mailCookies },
                    { "allCookies", _allCookies },
                    { "userDomainId", _userDomainId },
                    { "rootFolderId", _rootFolderId },
                    { "cookieCount", _cookieCount },
                    { "hasAuth", hasAuth },
                    { "hasSso", _hasSso },
                    { "hasCloudToken", _hasCloudToken },
                    { "loggedIn", loggedIn },
                    { "updatedAt", _updated == DateTime.MinValue ? "" : _updated.ToString("HH:mm:ss") }
                };
            }
        }
    }

    // ------------------------------------------------------------- 浏览器 / CDP

    internal sealed class BrowserManager
    {
        private readonly CaptureStore _capture;
        private readonly object _lock = new object();
        private Process _proc;
        private string _exe;
        private string _name;
        private string _profile;
        private int _debugPort;
        private string _error = "";
        private CdpClient _cdp;
        private bool _loopStarted;
        private bool _closed;

        private readonly ConcurrentDictionary<string, string> _sessions =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _attaching =
            new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string> _requestUrls =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public BrowserManager(CaptureStore capture)
        {
            _capture = capture;
        }

        public Dictionary<string, object> State()
        {
            bool running = false;
            try { running = _proc != null && !_proc.HasExited; } catch { }
            return new Dictionary<string, object>
            {
                { "available", _exe != null },
                { "running", running },
                { "connected", _cdp != null && _cdp.IsConnected },
                { "name", _name ?? "" },
                { "debugPort", _debugPort },
                { "error", _error ?? "" }
            };
        }

        public void Launch(string homeUrl)
        {
            lock (_lock)
            {
                if (_proc != null && !_proc.HasExited && _cdp != null && _cdp.IsConnected) return;
                if (_exe == null) FindBrowser();
                if (_exe == null)
                {
                    _error = "未找到 Chrome / Edge 浏览器，无法打开登录窗口";
                    throw new Exception(_error);
                }

                _closed = false;
                _error = "";
                _profile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Openlist-139", "browser-profile");
                try { Directory.CreateDirectory(_profile); } catch { }

                string portFile = Path.Combine(_profile, "DevToolsActivePort");
                try { if (File.Exists(portFile)) File.Delete(portFile); } catch { }

                var psi = new ProcessStartInfo(_exe);
                psi.UseShellExecute = false;
                psi.Arguments =
                    "--user-data-dir=\"" + _profile + "\" " +
                    "--remote-debugging-port=0 " +
                    "--no-first-run --no-default-browser-check --disable-session-crashed-bubble " +
                    "--disable-features=Translate,MediaRouter " +
                    "--app=\"" + homeUrl + "\" --window-size=1220,900";
                try
                {
                    _proc = Process.Start(psi);
                }
                catch (Exception ex)
                {
                    _error = "启动浏览器失败：" + ex.Message;
                    throw new Exception(_error);
                }

                Program.Log("已启动 " + (_name ?? "浏览器") + "，配置目录 " + _profile);
                string capturedPortFile = portFile;
                Task.Run(delegate { ConnectAsync(capturedPortFile); });
            }
        }

        public void OpenLogin(string homeUrl)
        {
            EnsureConnected(homeUrl);
            try
            {
                CreateTarget("https://yun.139.com/", true);
            }
            catch
            {
                CreateTarget("https://yun.139.com/", false);
            }
        }

        public void OpenUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) throw new Exception("缺少网址");
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https")
                throw new Exception("只允许打开 https 网址");
            string host = uri.Host.ToLowerInvariant();
            if (!(host == "139.com" || host.EndsWith(".139.com", StringComparison.Ordinal) ||
                  host == "10086.cn" || host.EndsWith(".10086.cn", StringComparison.Ordinal)))
                throw new Exception("只允许打开中国移动云盘 / 139 邮箱相关网址");
            EnsureConnected("http://127.0.0.1:" + ProgramState.Port + "/");
            CreateTarget(url, true);
        }

        public void RefreshCookies()
        {
            if (_cdp == null || !_cdp.IsConnected) return;
            try
            {
                Dictionary<string, object> result = _cdp.Send("Storage.getCookies", null, null);
                object[] cookies = Util.Array(result, "cookies");
                _capture.SetCookies(cookies);
            }
            catch (Exception ex)
            {
                Program.Log("刷新 Cookie 失败: " + ex.Message);
            }
        }

        public void Close()
        {
            _closed = true;
            try
            {
                if (_cdp != null && _cdp.IsConnected)
                    _cdp.Send("Browser.close", null, null);
            }
            catch { }
            Thread.Sleep(400);
            try
            {
                if (_proc != null && !_proc.HasExited)
                {
                    _proc.CloseMainWindow();
                    if (!_proc.WaitForExit(1500)) _proc.Kill();
                }
            }
            catch { }
        }

        private void EnsureConnected(string homeUrl)
        {
            if (_proc == null || _proc.HasExited || _cdp == null || !_cdp.IsConnected)
                Launch(homeUrl);

            DateTime deadline = DateTime.Now.AddSeconds(20);
            while ((_cdp == null || !_cdp.IsConnected) && DateTime.Now < deadline)
            {
                if (_proc != null && _proc.HasExited) break;
                Thread.Sleep(200);
            }
            if (_cdp == null || !_cdp.IsConnected)
                throw new Exception(_error.Length > 0 ? _error : "浏览器连接超时，请重试");
        }

        private void CreateTarget(string url, bool newWindow)
        {
            if (_cdp == null || !_cdp.IsConnected) throw new Exception("浏览器尚未连接");
            var p = new Dictionary<string, object> { { "url", url } };
            if (newWindow) p["newWindow"] = true;
            _cdp.Send("Target.createTarget", p, null);
        }

        private async Task ConnectAsync(string portFile)
        {
            try
            {
                int port = 0;
                string wsPath = null;
                for (int i = 0; i < 150; i++)
                {
                    if (_proc != null && _proc.HasExited)
                    {
                        _error = "浏览器已退出（可能被安全软件拦截）";
                        return;
                    }
                    try
                    {
                        if (File.Exists(portFile))
                        {
                            string[] lines = File.ReadAllLines(portFile);
                            if (lines.Length >= 2)
                            {
                                int p;
                                if (int.TryParse(lines[0].Trim(), out p) && p > 0 && lines[1].Trim().Length > 0)
                                {
                                    port = p;
                                    wsPath = lines[1].Trim();
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                    await Task.Delay(200);
                }

                if (wsPath == null)
                {
                    _error = "连接浏览器调试端口超时，请关闭工具后重试";
                    return;
                }

                _debugPort = port;
                var cdp = new CdpClient("ws://127.0.0.1:" + port + wsPath);
                cdp.Event += OnCdpEvent;
                cdp.Closed += delegate { _error = "浏览器调试连接已断开，请重新打开登录窗口"; };
                await cdp.ConnectAsync();
                _cdp = cdp;
                Program.Log("已连接浏览器调试端口 " + port);

                await cdp.SendAsync("Target.setDiscoverTargets",
                    new Dictionary<string, object> { { "discover", true } }, null);

                try
                {
                    Dictionary<string, object> targets = await cdp.SendAsync("Target.getTargets", null, null);
                    object[] infos = Util.Array(targets, "targetInfos");
                    foreach (object o in infos)
                    {
                        var info = o as Dictionary<string, object>;
                        if (info == null) continue;
                        if (Util.Str(info, "type") == "page") QueueAttach(Util.Str(info, "targetId"));
                    }
                }
                catch { }

                await RefreshCookiesAsync();

                if (!_loopStarted)
                {
                    _loopStarted = true;
                    Task.Run(delegate { CookieLoop(); });
                }
            }
            catch (Exception ex)
            {
                _error = "连接浏览器失败：" + ex.Message;
                Program.Log(_error);
            }
        }

        private async Task CookieLoop()
        {
            while (!_closed)
            {
                await Task.Delay(2500);
                if (_closed) break;
                try
                {
                    if (_proc != null && _proc.HasExited)
                    {
                        _error = "浏览器已关闭";
                        break;
                    }
                    await RefreshCookiesAsync();
                }
                catch { }
            }
        }

        private async Task RefreshCookiesAsync()
        {
            if (_cdp == null || !_cdp.IsConnected) return;
            try
            {
                Dictionary<string, object> result = await _cdp.SendAsync("Storage.getCookies", null, null);
                _capture.SetCookies(Util.Array(result, "cookies"));
            }
            catch (Exception ex)
            {
                Program.Log("后台刷新 Cookie 失败: " + ex.Message);
            }
        }

        private void OnCdpEvent(string method, Dictionary<string, object> prms, string sessionId)
        {
            try
            {
                if (method == "Target.targetCreated" || method == "Target.targetInfoChanged")
                {
                    var info = Util.Dict(prms, "targetInfo");
                    if (Util.Str(info, "type") == "page")
                        QueueAttach(Util.Str(info, "targetId"));
                }
                else if (method == "Target.detachedFromTarget")
                {
                    string targetId = Util.Str(prms, "targetId");
                    string sid;
                    if (targetId.Length > 0) _sessions.TryRemove(targetId, out sid);
                }
                else if (method == "Network.requestWillBeSent")
                {
                    HandleRequest(prms);
                }
                else if (method == "Network.requestWillBeSentExtraInfo")
                {
                    string requestId = Util.Str(prms, "requestId");
                    string url;
                    if (requestId.Length > 0 && _requestUrls.TryGetValue(requestId, out url) && IsCloudUrl(url))
                    {
                        var headers = Util.Dict(prms, "headers");
                        string auth = FindAuthorization(headers);
                        if (auth != null) _capture.SetAuthorization(auth);
                    }
                }
                else if (method == "Network.loadingFinished" || method == "Network.loadingFailed")
                {
                    string requestId = Util.Str(prms, "requestId");
                    string ignored;
                    if (requestId.Length > 0) _requestUrls.TryRemove(requestId, out ignored);
                }
            }
            catch (Exception ex)
            {
                Program.Log("处理 CDP 事件失败: " + ex.Message);
            }
        }

        private void HandleRequest(Dictionary<string, object> prms)
        {
            var request = Util.Dict(prms, "request");
            string url = Util.Str(request, "url");
            string requestId = Util.Str(prms, "requestId");
            if (requestId.Length > 0 && _requestUrls.Count < 2000)
                _requestUrls[requestId] = url;

            if (!IsCloudUrl(url)) return;

            string auth = FindAuthorization(Util.Dict(request, "headers"));
            if (auth != null) _capture.SetAuthorization(auth);

            string postData = Util.Str(request, "postData");
            if (postData.Length > 0 && url.IndexOf("/file/list", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    var body = new JavaScriptSerializer().DeserializeObject(postData) as Dictionary<string, object>;
                    if (body != null)
                    {
                        string folder = Util.Str(body, "parentFileId");
                        if (folder.Length > 0) _capture.SetRootFolderId(folder);
                    }
                }
                catch { }
            }
        }

        private static string FindAuthorization(Dictionary<string, object> headers)
        {
            if (headers == null) return null;
            foreach (KeyValuePair<string, object> kv in headers)
            {
                if (kv.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                {
                    string v = Convert.ToString(kv.Value);
                    if (!string.IsNullOrEmpty(v)) return v;
                }
            }
            return null;
        }

        private static bool IsCloudUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            return url.IndexOf("139.com", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   url.IndexOf("10086.cn", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void QueueAttach(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            if (_sessions.ContainsKey(targetId)) return;
            if (!_attaching.TryAdd(targetId, true)) return;
            Task.Run(delegate { AttachAsync(targetId); });
        }

        private async Task AttachAsync(string targetId)
        {
            try
            {
                if (_cdp == null || !_cdp.IsConnected) return;
                Dictionary<string, object> res = await _cdp.SendAsync("Target.attachToTarget",
                    new Dictionary<string, object> { { "targetId", targetId }, { "flatten", true } }, null);
                string sid = Util.Str(res, "sessionId");
                if (sid.Length > 0)
                {
                    _sessions[targetId] = sid;
                    try
                    {
                        await _cdp.SendAsync("Network.enable", new Dictionary<string, object>(), sid);
                        await _cdp.SendAsync("Page.enable", new Dictionary<string, object>(), sid);
                    }
                    catch { }
                    Program.Log("已监听页面: " + targetId);
                }
            }
            catch (Exception ex)
            {
                if (ex.Message.IndexOf("already attached", StringComparison.OrdinalIgnoreCase) < 0)
                    Program.Log("附加页面失败: " + ex.Message);
            }
            finally
            {
                bool ignored;
                _attaching.TryRemove(targetId, out ignored);
            }
        }

        private void FindBrowser()
        {
            string[] appPaths = { "chrome.exe", "msedge.exe", "brave.exe", "chromium.exe" };
            foreach (string exe in appPaths)
            {
                string path = FindInRegistry(exe);
                if (path != null)
                {
                    _exe = path;
                    _name = FriendlyName(exe);
                    return;
                }
            }

            string[] candidates =
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe")
            };
            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(c))
                    {
                        _exe = c;
                        _name = c.IndexOf("msedge", StringComparison.OrdinalIgnoreCase) >= 0 ? "Microsoft Edge" : "Google Chrome";
                        return;
                    }
                }
                catch { }
            }
        }

        private static string FriendlyName(string exe)
        {
            if (exe.StartsWith("msedge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
            if (exe.StartsWith("brave", StringComparison.OrdinalIgnoreCase)) return "Brave";
            return "Google Chrome";
        }

        private static string FindInRegistry(string exe)
        {
            string[] roots = { @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\" + exe };
            RegistryKey[] bases = { Registry.CurrentUser, Registry.LocalMachine };
            foreach (RegistryKey b in bases)
            {
                foreach (string r in roots)
                {
                    try
                    {
                        using (RegistryKey k = b.OpenSubKey(r))
                        {
                            if (k != null)
                            {
                                string v = k.GetValue(null) as string;
                                if (!string.IsNullOrEmpty(v) && File.Exists(v)) return v;
                            }
                        }
                    }
                    catch { }
                }
            }
            return null;
        }
    }

    internal static class ProgramState
    {
        public static int Port;
    }

    // ------------------------------------------------------------- CDP 客户端

    internal sealed class CdpClient
    {
        private readonly string _url;
        private readonly ClientWebSocket _ws = new ClientWebSocket();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> _pending =
            new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private int _nextId;
        private bool _closedRaised;

        public event Action<string, Dictionary<string, object>, string> Event;
        public event Action Closed;

        public CdpClient(string url)
        {
            _url = url;
        }

        public bool IsConnected
        {
            get { return _ws.State == WebSocketState.Open; }
        }

        public async Task ConnectAsync()
        {
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await _ws.ConnectAsync(new Uri(_url), CancellationToken.None);
            Task.Run(delegate { ReceiveLoop(); });
        }

        public Dictionary<string, object> Send(string method, Dictionary<string, object> prms, string sessionId)
        {
            Task<Dictionary<string, object>> t = SendAsync(method, prms, sessionId);
            try
            {
                t.Wait();
            }
            catch (AggregateException ae)
            {
                throw new Exception(ae.InnerException != null ? ae.InnerException.Message : ae.Message);
            }
            return t.Result;
        }

        public async Task<Dictionary<string, object>> SendAsync(string method, Dictionary<string, object> prms, string sessionId)
        {
            if (!IsConnected) throw new Exception("浏览器调试连接未建立");
            int id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<Dictionary<string, object>>();
            _pending[id] = tcs;

            var msg = new Dictionary<string, object>
            {
                { "id", id },
                { "method", method },
                { "params", prms ?? new Dictionary<string, object>() }
            };
            if (!string.IsNullOrEmpty(sessionId)) msg["sessionId"] = sessionId;

            byte[] data = Encoding.UTF8.GetBytes(Util.Serialize(msg));
            try
            {
                await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                TaskCompletionSource<Dictionary<string, object>> removed;
                _pending.TryRemove(id, out removed);
                throw new Exception("发送浏览器指令失败：" + ex.Message);
            }

            Task completed = await Task.WhenAny(tcs.Task, Task.Delay(20000));
            if (completed != tcs.Task)
            {
                TaskCompletionSource<Dictionary<string, object>> removed;
                _pending.TryRemove(id, out removed);
                throw new Exception("浏览器响应超时：" + method);
            }
            return await tcs.Task;
        }

        private async Task ReceiveLoop()
        {
            var buffer = new byte[65536];
            var ms = new MemoryStream();
            try
            {
                while (_ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult res;
                    try
                    {
                        res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    }
                    catch { break; }

                    if (res.MessageType == WebSocketMessageType.Close) break;
                    if (res.Count > 0) ms.Write(buffer, 0, res.Count);
                    if (!res.EndOfMessage) continue;

                    string text = Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0);
                    HandleMessage(text);
                }
            }
            catch { }
            finally
            {
                if (!_closedRaised)
                {
                    _closedRaised = true;
                    try { if (Closed != null) Closed(); } catch { }
                }
            }
        }

        private void HandleMessage(string text)
        {
            Dictionary<string, object> msg;
            try
            {
                msg = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }
                    .DeserializeObject(text) as Dictionary<string, object>;
            }
            catch { return; }
            if (msg == null) return;

            object idObj;
            if (msg.TryGetValue("id", out idObj) && idObj != null)
            {
                int id;
                try { id = Convert.ToInt32(idObj); } catch { return; }
                TaskCompletionSource<Dictionary<string, object>> tcs;
                if (!_pending.TryRemove(id, out tcs)) return;

                object errObj;
                if (msg.TryGetValue("error", out errObj) && errObj != null)
                {
                    var err = errObj as Dictionary<string, object>;
                    string message = err != null ? Util.Str(err, "message") : Convert.ToString(errObj);
                    tcs.SetException(new Exception(message));
                }
                else
                {
                    tcs.SetResult(Util.Dict(msg, "result"));
                }
                return;
            }

            string method = Util.Str(msg, "method");
            if (method.Length == 0) return;
            string sessionId = Util.Str(msg, "sessionId");
            Action<string, Dictionary<string, object>, string> handler = Event;
            if (handler != null)
            {
                try { handler(method, Util.Dict(msg, "params"), sessionId.Length > 0 ? sessionId : null); }
                catch { }
            }
        }
    }

    // ------------------------------------------------------------- 工具函数

    internal static class Util
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };

        public static string Serialize(object o)
        {
            lock (Serializer) { return Serializer.Serialize(o); }
        }

        public static object Deserialize(string s)
        {
            lock (Serializer) { return Serializer.DeserializeObject(s); }
        }

        public static Dictionary<string, object> Dict(Dictionary<string, object> d, string key)
        {
            if (d == null) return new Dictionary<string, object>();
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return new Dictionary<string, object>();
            var r = v as Dictionary<string, object>;
            return r ?? new Dictionary<string, object>();
        }

        public static object[] Array(Dictionary<string, object> d, string key)
        {
            if (d == null) return new object[0];
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return new object[0];
            var a = v as object[];
            return a ?? new object[0];
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            if (d == null) return "";
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v);
        }

        public static int Int(Dictionary<string, object> d, string key)
        {
            if (d == null) return 0;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        public static bool Bool(Dictionary<string, object> d, string key)
        {
            if (d == null) return false;
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return false;
            try { return Convert.ToBoolean(v); } catch { return false; }
        }

        public static string NormalizeBase(string url)
        {
            if (url == null) return "";
            url = url.Trim();
            if (url.Length == 0) return "";
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;
            return url.TrimEnd('/');
        }

        public static string NormalizeMount(string path)
        {
            if (path == null) return "";
            path = path.Replace('\\', '/').Trim();
            if (path.Length == 0) return "";
            if (!path.StartsWith("/")) path = "/" + path;
            while (path.Length > 1 && path.EndsWith("/")) path = path.Substring(0, path.Length - 1);
            return path;
        }
    }

    internal sealed class HttpResult
    {
        public int Status;
        public Dictionary<string, object> Json;
        public string Raw = "";
        public string Error = "";

        public string ErrorText()
        {
            if (Error.Length > 0) return Error;
            if (Status == 401) return "Token 无效或已过期";
            if (Status == 404) return "接口不存在，请检查 OpenList 地址是否正确";
            if (Raw.Length > 0)
            {
                string s = Raw.Trim();
                if (s.Length > 220) s = s.Substring(0, 220) + "...";
                return "返回异常（HTTP " + Status + "）：" + s;
            }
            return "请求失败（HTTP " + Status + "）";
        }
    }

    internal static class HttpUtil
    {
        public static HttpResult Call(string baseUrl, string path, string token, Dictionary<string, object> body, bool ignoreCert)
        {
            var result = new HttpResult();
            try
            {
                ServicePointManager.ServerCertificateValidationCallback = ignoreCert
                    ? new RemoteCertificateValidationCallback(delegate { return true; })
                    : null;

                var req = (HttpWebRequest)WebRequest.Create(baseUrl + path);
                req.Method = body == null ? "GET" : "POST";
                req.Timeout = 25000;
                req.ReadWriteTimeout = 25000;
                req.UserAgent = "Openlist-139/1.0";
                req.Accept = "application/json";
                if (!string.IsNullOrEmpty(token)) req.Headers["Authorization"] = token;

                if (body != null)
                {
                    byte[] data = Encoding.UTF8.GetBytes(Util.Serialize(body));
                    req.ContentType = "application/json; charset=utf-8";
                    req.ContentLength = data.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                }

                try
                {
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        result.Status = (int)resp.StatusCode;
                        result.Raw = ReadAll(resp);
                    }
                }
                catch (WebException wex)
                {
                    var resp = wex.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        result.Status = (int)resp.StatusCode;
                        result.Raw = ReadAll(resp);
                    }
                    else
                    {
                        result.Error = FriendlyWebError(wex);
                        return result;
                    }
                }

                if (!string.IsNullOrEmpty(result.Raw))
                {
                    try
                    {
                        result.Json = Util.Deserialize(result.Raw) as Dictionary<string, object>;
                    }
                    catch { }
                    if (result.Json == null && result.Error.Length == 0)
                        result.Error = "返回内容不是 JSON，请确认地址是 OpenList（HTTP " + result.Status + "）";
                }
                return result;
            }
            catch (Exception ex)
            {
                result.Error = "请求失败：" + ex.Message;
                return result;
            }
        }

        private static string ReadAll(HttpWebResponse resp)
        {
            using (Stream stream = resp.GetResponseStream())
            {
                if (stream == null) return "";
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        private static string FriendlyWebError(WebException wex)
        {
            switch (wex.Status)
            {
                case WebExceptionStatus.NameResolutionFailure:
                    return "无法解析 OpenList 域名，请检查地址";
                case WebExceptionStatus.ConnectFailure:
                    return "无法连接 OpenList，请检查地址、端口和网络";
                case WebExceptionStatus.Timeout:
                    return "连接 OpenList 超时";
                case WebExceptionStatus.TrustFailure:
                    return "HTTPS 证书校验失败，可勾选“忽略证书错误”后重试";
                default:
                    return "连接 OpenList 失败：" + wex.Message;
            }
        }
    }

    // ------------------------------------------------------------- 本地 HTTP 服务

    internal sealed class HttpRequest
    {
        public string Method = "";
        public string Path = "";
        public string QueryString = "";
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Body = "";

        public Dictionary<string, object> JsonBody()
        {
            if (string.IsNullOrEmpty(Body)) return new Dictionary<string, object>();
            try
            {
                var d = Util.Deserialize(Body) as Dictionary<string, object>;
                return d ?? new Dictionary<string, object>();
            }
            catch { return new Dictionary<string, object>(); }
        }
    }

    internal sealed class HttpResponse
    {
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body = new byte[0];

        public static HttpResponse JsonObject(object obj, int status)
        {
            return new HttpResponse
            {
                Status = status,
                ContentType = "application/json; charset=utf-8",
                Body = Encoding.UTF8.GetBytes(Util.Serialize(obj))
            };
        }

        public static HttpResponse Text(string text, string contentType, int status)
        {
            return new HttpResponse
            {
                Status = status,
                ContentType = contentType,
                Body = Encoding.UTF8.GetBytes(text ?? "")
            };
        }

        public static HttpResponse Asset(string resourceName, string contentType, string fallbackFile)
        {
            byte[] bytes = null;
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(resourceName))
                {
                    if (s != null)
                    {
                        using (var ms = new MemoryStream())
                        {
                            s.CopyTo(ms);
                            bytes = ms.ToArray();
                        }
                    }
                }
            }
            catch { }

            if (bytes == null)
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "src", "web", fallbackFile);
                if (!File.Exists(p))
                    p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web", fallbackFile);
                if (File.Exists(p)) bytes = File.ReadAllBytes(p);
            }

            if (bytes == null) return Text("资源缺失：" + fallbackFile, "text/plain; charset=utf-8", 500);

            return new HttpResponse { Status = 200, ContentType = contentType, Body = bytes };
        }
    }

    internal sealed class HttpServer
    {
        private readonly TcpListener _listener;
        private readonly Func<HttpRequest, HttpResponse> _handler;
        private readonly int _port;

        public HttpServer(int port, Func<HttpRequest, HttpResponse> handler)
        {
            _port = port;
            _handler = handler;
            _listener = new TcpListener(IPAddress.Loopback, port);
        }

        public void Start()
        {
            _listener.Start();
            ProgramState.Port = _port;
            Task.Run(delegate { AcceptLoop(); });
        }

        public static int FindFreePort(int start)
        {
            for (int p = start; p < start + 80; p++)
            {
                try
                {
                    var l = new TcpListener(IPAddress.Loopback, p);
                    l.Start();
                    l.Stop();
                    return p;
                }
                catch { }
            }
            throw new Exception("找不到可用的本地端口");
        }

        private async Task AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch { break; }

                Task.Run(delegate { HandleClient(client); });
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    NetworkStream stream = client.GetStream();

                    var headerBuffer = new MemoryStream();
                    var buffer = new byte[8192];
                    int headerEnd = -1;

                    while (headerEnd < 0)
                    {
                        int n = stream.Read(buffer, 0, buffer.Length);
                        if (n <= 0) return;
                        headerBuffer.Write(buffer, 0, n);
                        byte[] cur = headerBuffer.GetBuffer();
                        int len = (int)headerBuffer.Length;
                        for (int i = 0; i + 3 < len; i++)
                        {
                            if (cur[i] == 13 && cur[i + 1] == 10 && cur[i + 2] == 13 && cur[i + 3] == 10)
                            {
                                headerEnd = i;
                                break;
                            }
                        }
                        if (headerBuffer.Length > 1024 * 1024) return;
                    }

                    byte[] all = headerBuffer.ToArray();
                    string headerText = Encoding.UTF8.GetString(all, 0, headerEnd);
                    string[] lines = headerText.Split(new string[] { "\r\n" }, StringSplitOptions.None);

                    var req = new HttpRequest();
                    string[] first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    req.Method = first[0].ToUpperInvariant();
                    string rawTarget = first[1];
                    int q = rawTarget.IndexOf('?');
                    if (q >= 0)
                    {
                        req.Path = rawTarget.Substring(0, q);
                        req.QueryString = rawTarget.Substring(q + 1);
                    }
                    else
                    {
                        req.Path = rawTarget;
                    }
                    req.Path = Uri.UnescapeDataString(req.Path);

                    for (int i = 1; i < lines.Length; i++)
                    {
                        int colon = lines[i].IndexOf(':');
                        if (colon <= 0) continue;
                        string key = lines[i].Substring(0, colon).Trim();
                        string val = lines[i].Substring(colon + 1).Trim();
                        if (key.Length > 0) req.Headers[key] = val;
                    }

                    int contentLength = 0;
                    string cl;
                    if (req.Headers.TryGetValue("Content-Length", out cl)) int.TryParse(cl, out contentLength);
                    int bodyStart = headerEnd + 4;
                    int already = all.Length - bodyStart;
                    if (already < 0) already = 0;
                    if (contentLength > 8 * 1024 * 1024) contentLength = 0;

                    var body = new MemoryStream();
                    if (already > 0)
                    {
                        int take = Math.Min(already, contentLength);
                        body.Write(all, bodyStart, take);
                    }
                    while (body.Length < contentLength)
                    {
                        int need = (int)(contentLength - body.Length);
                        int n = stream.Read(buffer, 0, Math.Min(buffer.Length, need));
                        if (n <= 0) break;
                        body.Write(buffer, 0, n);
                    }
                    if (contentLength > 0)
                        req.Body = Encoding.UTF8.GetString(body.ToArray());

                    HttpResponse response;
                    try { response = _handler(req); }
                    catch (Exception ex)
                    {
                        response = HttpResponse.JsonObject(
                            new Dictionary<string, object> { { "ok", false }, { "message", ex.Message } }, 500);
                    }

                    byte[] outBody = response.Body ?? new byte[0];
                    string reason = response.Status == 200 ? "OK" :
                                    response.Status == 204 ? "No Content" :
                                    response.Status == 404 ? "Not Found" :
                                    response.Status == 405 ? "Method Not Allowed" : "Error";
                    string head =
                        "HTTP/1.1 " + response.Status + " " + reason + "\r\n" +
                        "Content-Type: " + response.ContentType + "\r\n" +
                        "Content-Length: " + outBody.Length + "\r\n" +
                        "Cache-Control: no-store\r\n" +
                        "X-Content-Type-Options: nosniff\r\n" +
                        "Connection: close\r\n\r\n";
                    byte[] headBytes = Encoding.ASCII.GetBytes(head);
                    stream.Write(headBytes, 0, headBytes.Length);
                    if (req.Method != "HEAD" && outBody.Length > 0)
                        stream.Write(outBody, 0, outBody.Length);
                    stream.Flush();
                }
            }
            catch (Exception ex)
            {
                Program.Log("连接处理失败: " + ex.Message);
            }
        }
    }
}
