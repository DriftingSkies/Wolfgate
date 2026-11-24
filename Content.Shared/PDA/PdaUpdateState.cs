using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared.PDA
{
    [Serializable, NetSerializable]
    public sealed class PdaUpdateState : CartridgeLoaderUiState // WTF is this. what. I ... fuck me I just want net entities to work
        // TODO purge this shit
        //AAAAAAAAAAAAAAAA
    {
        public bool FlashlightEnabled;
        public bool HasPen;
        public bool HasPai;
        public PdaIdInfoText PdaOwnerInfo;
        public string? StationName;
        public bool HasUplink;
        public bool CanPlayMusic;
        public string? Address;
        public int Balance; // Frontier
        public string? OwnedShipName; // Frontier
        public bool NotificationsEnabled; // WOLFGATE(PdaUi): program notification setting
        public bool HasDisk; // WOLFGATE(PdaStorage): data disk inserted

        public PdaUpdateState(
            List<NetEntity> programs,
            NetEntity? activeUI,
            bool flashlightEnabled,
            bool hasPen,
            bool hasPai,
            PdaIdInfoText pdaOwnerInfo,
            int balance, // Frontier
            string? ownedShipName, // Frontier
            string? stationName,
            bool hasUplink = false,
            bool canPlayMusic = false,
            string? address = null,
            bool notificationsEnabled = true, // WOLFGATE(PdaUi): program notification setting
            bool hasDisk = false) // WOLFGATE(PdaStorage): data disk inserted
            : base(programs, activeUI)
        {
            FlashlightEnabled = flashlightEnabled;
            HasPen = hasPen;
            HasPai = hasPai;
            PdaOwnerInfo = pdaOwnerInfo;
            HasUplink = hasUplink;
            CanPlayMusic = canPlayMusic;
            StationName = stationName;
            Address = address;
            Balance = balance; // Frontier
            OwnedShipName = ownedShipName; // Frontier
            NotificationsEnabled = notificationsEnabled; // WOLFGATE(PdaUi)
            HasDisk = hasDisk; // WOLFGATE(PdaStorage)
        }
    }

    [Serializable, NetSerializable]
    public struct PdaIdInfoText
    {
        public string? ActualOwnerName;
        public string? IdOwner;
        public string? JobTitle;
        public string? CompanyName;
        public Color CompanyColor;
        public string? StationAlertLevel;
        public Color StationAlertColor;
        public string? WarLevel;
        public DateTime? CurrentDate; // DeltaV - PDA date
    }
}
