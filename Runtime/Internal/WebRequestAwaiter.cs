using System.Threading.Tasks;
using UnityEngine.Networking;

namespace TestingFloor.Internal {
    internal static class WebRequestAwaiter {
        public static Task<UnityWebRequest> SendAsync(UnityWebRequest req) {
            var tcs = new TaskCompletionSource<UnityWebRequest>();
            var op = req.SendWebRequest();
            op.completed += _ => tcs.TrySetResult(req);
            return tcs.Task;
        }
    }
}
