#if UNITY_EDITOR

namespace MgDataKit.Editor {
    internal sealed class FeishuPlayModeSyncProvider : IMgDataPlayModeSyncProvider {
        public bool TrySyncBeforePlay(out string errorMessage) {
            errorMessage = null;
            if (!MgDataFeishuSyncService.GetPlayBeforeFeishuSyncEnabled())
                return true;

            return MgDataFeishuSyncService.TrySyncAllWithPreflight(out errorMessage);
        }
    }
}

#endif
