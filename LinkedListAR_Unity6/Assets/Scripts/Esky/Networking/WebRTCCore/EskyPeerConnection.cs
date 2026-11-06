// =====================================================================================================
// [MIGRATION: Microsoft MixedReality-WebRTC  ->  com.unity.webrtc]            (NEW FILE)
//
// Why this file exists
// --------------------
// The original WebRTCAutoDiscoveryHandler derived from Microsoft.MixedReality.WebRTC.Unity.Signaler and
// drove the Microsoft PeerConnection MonoBehaviour (Peer.AddDataChannelAsync, Peer.CreateOffer,
// HandleConnectionMessageAsync, Peer.AddIceCandidate ...). Esky only ever used WebRTC for
//   * three pre-negotiated, ordered + reliable DATA CHANNELS (ids 0/1/2), and
//   * SDP offer / answer / ICE candidate exchange over its own HTTP "auto discovery" signaling.
// It never used audio, video, media lines or transceivers.
//
// com.unity.webrtc exposes a W3C-style API (RTCPeerConnection, RTCDataChannel, coroutine-based async
// operations) instead of Task-based callbacks, and has no PeerConnection MonoBehaviour. This class is the
// thin adapter that gives the handler the same *behaviour* it had before:
//
//   MR-WebRTC (old)                                             this class (new)
//   ----------------------------------------------------------  -----------------------------------------
//   PeerConnection component + InitializeAsync()                new RTCPeerConnection(config)  (ctor)
//   Peer.AddDataChannelAsync(id,label,ordered:true,reliable:true) CreateDataChannels() (negotiated, same ids)
//   Peer.CreateOffer() / PeerConnection.StartConnection()       CreateOffer()  (CreateOffer + SetLocalDescription)
//   PeerConnection.HandleConnectionMessageAsync(offer)+CreateAnswer() HandleOffer()
//   PeerConnection.HandleConnectionMessageAsync(answer)         HandleAnswer()
//   Peer.IceCandidateReadytoSend / LocalSdpReadytoSend          LocalIceCandidateReady / LocalOfferReady / LocalAnswerReady
//   DataChannel.MessageReceived / StateChanged                  MessageReceived / ChannelStateChanged
//   DataChannel.SendMessage(byte[])                             Send(channelId, byte[])
//
// It lives in its own assembly (BEERLabs.ProjectEsky.Networking.WebRTC.Core) so that the PlayMode tests in
// Assets/Tests/PlayMode can reference it (a test assembly cannot reference Assembly-CSharp).
// The namespace is deliberately unchanged so that existing Esky code keeps compiling.
// =====================================================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;

namespace BEERLabs.ProjectEsky.Networking.WebRTC.Discovery
{
    // [MIGRATION] MOVED, NOT CHANGED: IceMessageWebsocket, SdpMessageWebsocket and WebrtcShakeClass used to be
    // declared in WebRTCAutoDiscoveryHandler.cs. They are moved here (same namespace, same fields, same order)
    // because EskyPeerConnection needs them and lives in a separate assembly. JsonUtility serialises fields in
    // declaration order, so the JSON that travels between Esky devices is byte-for-byte what it was before
    // (this is asserted by EskyWebRtcMigrationTests.WireFormat_IsUnchanged).
    [System.Serializable]
    public class IceMessageWebsocket{
        public string candidate;
        public int sdpMlineIndex;
        public string sdpMid;
        public IceMessageWebsocket(string cand, int sdpmline, string sdpmid){
            candidate = cand;
            sdpMlineIndex = sdpmline;
            sdpMid = sdpmid;
        }
    }
    [System.Serializable]
    public class SdpMessageWebsocket{
        public string sdp;
        public string type;
        public SdpMessageWebsocket(string sdpMessage, string sdpType){
            sdp = sdpMessage;
            type = sdpType;
        }
    }

    [System.Serializable]
    public class WebrtcShakeClass{
        public List<IceMessageWebsocket> iceMessages;
        public SdpMessageWebsocket sdpMessage;
    }

    /// <summary>
    /// [MIGRATION - NEW CLASS] One Esky WebRTC session: a single RTCPeerConnection carrying the three Esky data
    /// channels. Replaces the Microsoft PeerConnection MonoBehaviour + Signaler plumbing.
    /// All callbacks are raised on the Unity main thread (com.unity.webrtc marshals native callbacks through the
    /// Unity synchronisation context), which is why the old "_mainThreadWorkQueue" of Signaler is not needed.
    /// A session is single-use: after it fails or is closed, Dispose() it and create a new one.
    /// </summary>
    public sealed class EskyPeerConnection : IDisposable
    {
        // Same ids and labels the original OnPeerInitialized() used with AddDataChannelAsync(id, label, true, true).
        public const int MessageTransferChannelId = 0;     // "message_transfer"; its state drives isConnected
        public const int UnreliableTransferChannelId = 1;  // "unreliabletransferChannel"; used by SendBytes AND SendBytesReliable (as before)
        public const int ReliableTransferChannelId = 2;    // "reliabletransferChannel"; created but unused (as before)
        public const int DataChannelCount = 3;
        static readonly string[] ChannelLabels = { "message_transfer", "unreliabletransferChannel", "reliabletransferChannel" };

        readonly RTCPeerConnection _peer;
        readonly RTCDataChannel[] _channels = new RTCDataChannel[DataChannelCount];
        readonly List<IceMessageWebsocket> _pendingRemoteIce = new List<IceMessageWebsocket>();
        bool _remoteDescriptionApplied;
        bool _disposed;

        /// <summary>Raised for every message on any of the three channels (old: DataChannel.MessageReceived).</summary>
        public event Action<int, byte[]> MessageReceived;
        /// <summary>Raised when a channel opens or closes (old: DataChannel.StateChanged).</summary>
        public event Action<int, RTCDataChannelState> ChannelStateChanged;
        /// <summary>A local ICE candidate was gathered (old: Signaler.OnIceCandidateReadyToSend).</summary>
        public event Action<IceMessageWebsocket> LocalIceCandidateReady;
        /// <summary>The local offer was created and applied (old: Signaler.OnSdpOfferReadyToSend).</summary>
        public event Action<string> LocalOfferReady;
        /// <summary>The local answer was created and applied (old: Signaler.OnSdpAnswerReadyToSend).</summary>
        public event Action<string> LocalAnswerReady;
        /// <summary>The remote offer was applied successfully, right before the answer is created.</summary>
        public event Action OfferApplied;
        /// <summary>Peer connection state changed (new: lets the handler recover from a failed ICE connection).</summary>
        public event Action<RTCPeerConnectionState> ConnectionStateChanged;

        public bool IsDisposed { get { return _disposed; } }
        /// <summary>True once SetRemoteDescription has succeeded for this session.</summary>
        public bool RemoteDescriptionApplied { get { return _remoteDescriptionApplied; } }

        /// <summary>
        /// [MIGRATION] Replaces PeerConnection.InitializeAsync(): the old code built a native peer from the component's
        /// IceServers list (one STUN entry "stun.l.google.com:19302"); here the same servers are passed explicitly.
        /// Unlike the old API the peer is available synchronously, so there is no OnInitialized event to wait for.
        /// </summary>
        public EskyPeerConnection(string[] iceServerUrls, string iceUsername, string iceCredential)
        {
            var config = new RTCConfiguration();
            if (iceServerUrls != null && iceServerUrls.Length > 0)
            {
                var server = new RTCIceServer();
                server.urls = iceServerUrls;
                if (!string.IsNullOrEmpty(iceUsername)) server.username = iceUsername;
                if (!string.IsNullOrEmpty(iceCredential))
                {
                    server.credential = iceCredential;
                    server.credentialType = RTCIceCredentialType.Password;
                }
                config.iceServers = new RTCIceServer[] { server };
            }
            _peer = new RTCPeerConnection(ref config);
            _peer.OnIceCandidate = OnIceCandidateInternal;
            _peer.OnConnectionStateChange = OnConnectionStateChangeInternal;
        }

        /// <summary>
        /// [MIGRATION] Replaces the three Peer.AddDataChannelAsync(id, label, ordered:true, reliable:true) calls.
        /// Passing an explicit id with negotiated=true gives the same "pre-negotiated, both sides create the same id"
        /// semantics as MR-WebRTC's id overload, so no in-band DCEP open handshake is needed and no OnDataChannel
        /// event is raised. ordered=true and no maxRetransmits / maxPacketLifeTime == reliable + ordered.
        /// </summary>
        public void CreateDataChannels()
        {
            for (int i = 0; i < DataChannelCount; i++)
            {
                int channelId = i;
                var init = new RTCDataChannelInit();
                init.ordered = true;
                init.negotiated = true;
                init.id = channelId;
                RTCDataChannel channel = _peer.CreateDataChannel(ChannelLabels[channelId], init);
                channel.OnMessage = bytes => { if (!_disposed && MessageReceived != null) MessageReceived(channelId, bytes); };
                channel.OnOpen = () => { if (!_disposed && ChannelStateChanged != null) ChannelStateChanged(channelId, RTCDataChannelState.Open); };
                channel.OnClose = () => { if (!_disposed && ChannelStateChanged != null) ChannelStateChanged(channelId, RTCDataChannelState.Closed); };
                channel.OnError = error => Debug.LogWarning("[EskyWebRTC] Data channel " + channelId + " error: " + error.message);
                _channels[channelId] = channel;
            }
        }

        /// <summary>The RTCDataChannel for an Esky channel id (0..2), or null.</summary>
        public RTCDataChannel GetChannel(int channelId)
        {
            if (channelId < 0 || channelId >= DataChannelCount) return null;
            return _channels[channelId];
        }

        /// <summary>Ready state of an Esky channel; Closed when the session or channel is gone.</summary>
        public RTCDataChannelState GetChannelState(int channelId)
        {
            RTCDataChannel channel = GetChannel(channelId);
            if (_disposed || channel == null) return RTCDataChannelState.Closed;
            return channel.ReadyState;
        }

        public RTCPeerConnectionState ConnectionState
        {
            get { return _disposed ? RTCPeerConnectionState.Closed : _peer.ConnectionState; }
        }

        /// <summary>
        /// [MIGRATION] Replaces DataChannel.SendMessage(byte[]). The old call threw if the channel was missing or closed;
        /// this returns false instead so callers that are already guarded by "isConnected" cannot crash on a race.
        /// </summary>
        public bool Send(int channelId, byte[] data)
        {
            if (_disposed || data == null) return false;
            RTCDataChannel channel = GetChannel(channelId);
            if (channel == null || channel.ReadyState != RTCDataChannelState.Open) return false;
            channel.Send(data);
            return true;
        }

        /// <summary>
        /// [MIGRATION] Replaces PeerConnection.StartConnection() / Peer.CreateOffer(). MR-WebRTC created the offer AND
        /// applied it as the local description internally and then fired LocalSdpReadytoSend; with com.unity.webrtc these
        /// are two explicit steps, after which LocalOfferReady is raised (so ICE gathering starts at the same moment).
        /// Run it with StartCoroutine(...).
        /// </summary>
        public IEnumerator CreateOffer()
        {
            if (_disposed) yield break;
            RTCSessionDescriptionAsyncOperation offerOp = null;
            string failure = null;
            try { offerOp = _peer.CreateOffer(); }
            catch (Exception ex) { failure = ex.Message; }
            if (failure != null) { Debug.LogError("[EskyWebRTC] CreateOffer failed: " + failure); yield break; }
            yield return offerOp;
            if (_disposed) yield break;
            if (offerOp.IsError) { Debug.LogError("[EskyWebRTC] CreateOffer failed: " + offerOp.Error.message); yield break; }

            RTCSessionDescription offer = offerOp.Desc;
            yield return SetLocalDescriptionRoutine(offer);
            if (_disposed || !_localApplied) yield break;
            if (LocalOfferReady != null) LocalOfferReady(offer.sdp);
        }

        /// <summary>
        /// [MIGRATION] Replaces HandleConnectionMessageAsync(offer).ContinueWith(CreateAnswer). Applies the remote offer,
        /// then the remote ICE candidates, then creates and applies the answer and raises LocalAnswerReady.
        /// Note the ordering difference: the old code called AddIceCandidate BEFORE the remote description; libwebrtc
        /// (and therefore com.unity.webrtc) rejects candidates until a remote description exists, so they are applied after.
        /// </summary>
        public IEnumerator HandleOffer(string offerSdp, IList<IceMessageWebsocket> remoteIce)
        {
            if (_disposed) yield break;
            AddRemoteIceCandidates(remoteIce);
            yield return SetRemoteDescriptionRoutine(RTCSdpType.Offer, offerSdp);
            if (_disposed || !_remoteDescriptionApplied) yield break;
            if (OfferApplied != null) OfferApplied();

            RTCSessionDescriptionAsyncOperation answerOp = null;
            string failure = null;
            try { answerOp = _peer.CreateAnswer(); }
            catch (Exception ex) { failure = ex.Message; }
            if (failure != null) { Debug.LogError("[EskyWebRTC] CreateAnswer failed: " + failure); yield break; }
            yield return answerOp;
            if (_disposed) yield break;
            if (answerOp.IsError) { Debug.LogError("[EskyWebRTC] CreateAnswer failed: " + answerOp.Error.message); yield break; }

            RTCSessionDescription answer = answerOp.Desc;
            yield return SetLocalDescriptionRoutine(answer);
            if (_disposed || !_localApplied) yield break;
            if (LocalAnswerReady != null) LocalAnswerReady(answer.sdp);
        }

        /// <summary>[MIGRATION] Replaces HandleConnectionMessageAsync(answer): applies the remote answer and its candidates.</summary>
        public IEnumerator HandleAnswer(string answerSdp, IList<IceMessageWebsocket> remoteIce)
        {
            if (_disposed) yield break;
            AddRemoteIceCandidates(remoteIce);
            yield return SetRemoteDescriptionRoutine(RTCSdpType.Answer, answerSdp);
        }

        /// <summary>
        /// [MIGRATION] Replaces Peer.AddIceCandidate(new IceCandidate{...}). Candidates that arrive before the remote
        /// description is applied are queued and flushed as soon as it is (see HandleOffer).
        /// </summary>
        public void AddRemoteIceCandidates(IList<IceMessageWebsocket> candidates)
        {
            if (_disposed || candidates == null) return;
            if (!_remoteDescriptionApplied)
            {
                for (int i = 0; i < candidates.Count; i++) _pendingRemoteIce.Add(candidates[i]);
                return;
            }
            for (int i = 0; i < candidates.Count; i++) AddIceCandidateNow(candidates[i]);
        }

        // ------------------------------------------------------------------------------------------------
        bool _localApplied;

        IEnumerator SetLocalDescriptionRoutine(RTCSessionDescription desc)
        {
            _localApplied = false;
            RTCSetSessionDescriptionAsyncOperation op = null;
            string failure = null;
            try { op = _peer.SetLocalDescription(ref desc); }
            catch (Exception ex) { failure = ex.Message; }
            if (failure != null) { Debug.LogError("[EskyWebRTC] SetLocalDescription failed: " + failure); yield break; }
            yield return op;
            if (_disposed) yield break;
            if (op.IsError) { Debug.LogError("[EskyWebRTC] SetLocalDescription failed: " + op.Error.message); yield break; }
            _localApplied = true;
        }

        IEnumerator SetRemoteDescriptionRoutine(RTCSdpType type, string sdp)
        {
            var desc = new RTCSessionDescription();
            desc.type = type;
            desc.sdp = sdp;
            RTCSetSessionDescriptionAsyncOperation op = null;
            string failure = null;
            try { op = _peer.SetRemoteDescription(ref desc); }
            catch (Exception ex) { failure = ex.Message; }
            if (failure != null) { Debug.LogError("[EskyWebRTC] Cannot apply remote description: " + failure); yield break; }
            yield return op;
            if (_disposed) yield break;
            if (op.IsError) { Debug.LogError("[EskyWebRTC] Cannot apply remote description: " + op.Error.message); yield break; }

            _remoteDescriptionApplied = true;
            for (int i = 0; i < _pendingRemoteIce.Count; i++) AddIceCandidateNow(_pendingRemoteIce[i]);
            _pendingRemoteIce.Clear();
        }

        void AddIceCandidateNow(IceMessageWebsocket message)
        {
            if (message == null) return;
            try
            {
                var init = new RTCIceCandidateInit();
                init.candidate = message.candidate;
                init.sdpMid = message.sdpMid;
                init.sdpMLineIndex = message.sdpMlineIndex;
                using (var candidate = new RTCIceCandidate(init))
                {
                    if (!_peer.AddIceCandidate(candidate))
                        Debug.LogWarning("[EskyWebRTC] Remote ICE candidate was rejected: " + message.candidate);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[EskyWebRTC] Could not add remote ICE candidate: " + ex.Message);
            }
        }

        void OnIceCandidateInternal(RTCIceCandidate candidate)
        {
            if (_disposed || candidate == null || LocalIceCandidateReady == null) return;
            int mLineIndex = candidate.SdpMLineIndex.HasValue ? candidate.SdpMLineIndex.Value : 0;
            LocalIceCandidateReady(new IceMessageWebsocket(candidate.Candidate, mLineIndex, candidate.SdpMid));
        }

        void OnConnectionStateChangeInternal(RTCPeerConnectionState state)
        {
            if (_disposed || ConnectionStateChanged == null) return;
            ConnectionStateChanged(state);
        }

        /// <summary>
        /// [MIGRATION] Replaces PeerConnection.Uninitialize(). Handlers are detached first so closing the channels does not
        /// raise state-change events into a handler that is already tearing the session down.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _peer.OnIceCandidate = null;
            _peer.OnConnectionStateChange = null;
            for (int i = 0; i < DataChannelCount; i++)
            {
                RTCDataChannel channel = _channels[i];
                if (channel == null) continue;
                channel.OnMessage = null;
                channel.OnOpen = null;
                channel.OnClose = null;
                channel.OnError = null;
                channel.Dispose();      // RTCDataChannel.Dispose() closes the channel first
                _channels[i] = null;
            }
            _peer.Dispose();        // RTCPeerConnection.Dispose() closes the connection first
        }
    }
}
