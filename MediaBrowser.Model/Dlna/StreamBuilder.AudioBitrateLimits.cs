namespace MediaBrowser.Model.Dlna
{
    /// <summary>
    /// Class StreamBuilder.
    /// </summary>
    public partial class StreamBuilder
    {
        private static int GetMaxAudioBitrateForTotalBitrate(long totalBitrate)
        {
            if (totalBitrate <= 640000)
            {
                return 128000;
            }

            if (totalBitrate <= 2000000)
            {
                return 384000;
            }

            if (totalBitrate <= 3000000)
            {
                return 448000;
            }

            if (totalBitrate <= 4000000)
            {
                return 640000;
            }

            return GetMaxAudioBitrateForHighTotalBitrate(totalBitrate);
        }

        private static int GetMaxAudioBitrateForHighTotalBitrate(long totalBitrate)
        {
            if (totalBitrate <= 5000000)
            {
                return 768000;
            }

            if (totalBitrate <= 10000000)
            {
                return 1536000;
            }

            if (totalBitrate <= 15000000)
            {
                return 2304000;
            }

            if (totalBitrate <= 20000000)
            {
                return 3584000;
            }

            return 7168000;
        }
    }
}
