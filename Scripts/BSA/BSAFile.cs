namespace OpenFo3.BSA
{
    public class BSAFile
    {
        public string Path { get; set; }
        public uint Size { get; set; }
        public uint Offset { get; set; }
        public ulong Hash { get; set; }

        public override string ToString()
        {
            return $"{Path} (Size: {Size}, Offset: 0x{Offset:X})";
        }
    }
}
