using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace WatsonWebsocket
{
    /// <summary>
    /// WatsonWebsocket statistics.
    /// </summary>
    public class Statistics
    {
        #region Public-Members

        /// <summary>
        /// The time at which the client or server was started.
        /// </summary>
        public DateTime StartTime { get; set; } = DateTime.Now.ToUniversalTime();

        /// <summary>
        /// The amount of time which the client or server has been up.
        /// </summary>
        public TimeSpan UpTime => DateTime.Now.ToUniversalTime() - StartTime;

        /// <summary>
        /// The number of bytes received.
        /// </summary>
        public long ReceivedBytes;

        /// <summary>
        /// The number of messages received.
        /// </summary>
        public long ReceivedMessages;

        /// <summary>
        /// Average received message size in bytes.
        /// </summary>
        public int ReceivedMessageSizeAverage
        {
            get
            {
                if (ReceivedBytes > 0 && ReceivedMessages > 0)
                {
                    return (int)(ReceivedBytes / ReceivedMessages);
                }

                return 0;
            }
        }

        /// <summary>
        /// The number of bytes sent.
        /// </summary>
        public long SentBytes;

        /// <summary>
        /// The number of messages sent.
        /// </summary>
        public long SentMessages;


        /// <summary>
        /// Average sent message size in bytes.
        /// </summary>
        public decimal SentMessageSizeAverage
        {
            get
            {
                if (SentBytes > 0 && SentMessages > 0)
                {
                    return (int)(SentBytes / SentMessages);
                }

                return 0;
            }
        }

        #endregion

        #region Private-Members


        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initialize the statistics object.
        /// </summary>
        public Statistics()
        {

        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Return human-readable version of the object.
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            var ret =
                "--- Statistics ---" + Environment.NewLine +
                "    Started     : " + StartTime + Environment.NewLine +
                "    Uptime      : " + UpTime + Environment.NewLine +
                "    Received    : " + Environment.NewLine +
                "       Bytes    : " + ReceivedBytes + Environment.NewLine +
                "       Messages : " + ReceivedMessages + Environment.NewLine +
                "       Average  : " + ReceivedMessageSizeAverage + " bytes" + Environment.NewLine +
                "    Sent        : " + Environment.NewLine +
                "       Bytes    : " + SentBytes + Environment.NewLine +
                "       Messages : " + SentMessages + Environment.NewLine +
                "       Average  : " + SentMessageSizeAverage + " bytes" + Environment.NewLine;
            return ret;
        }

        /// <summary>
        /// Reset statistics other than StartTime and UpTime.
        /// </summary>
        public void Reset()
        {
            ReceivedBytes = 0;
            ReceivedMessages = 0;
            SentBytes = 0;
            SentMessages = 0;
        }

        #endregion

        #region Internal-Methods

        internal void IncrementReceivedMessages()
        {
            ReceivedMessages = Interlocked.Increment(ref ReceivedMessages);
        }

        internal void IncrementSentMessages()
        {
            SentMessages = Interlocked.Increment(ref SentMessages);
        }

        internal void AddReceivedBytes(long bytes)
        {
            ReceivedBytes = Interlocked.Add(ref ReceivedBytes, bytes);
        }

        internal void AddSentBytes(long bytes)
        {
            SentBytes = Interlocked.Add(ref SentBytes, bytes);
        }

        #endregion

        #region Private-Methods

        #endregion
    }
}
