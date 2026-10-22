using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.Sockets;
using System;
using System.Net;
using System.Text;
using System.Linq;
using System.ComponentModel;
using System.Threading;
// [MIGRATION] CHANGED: was "using Microsoft.MixedReality.WebRTC;". Esky now uses com.unity.webrtc (namespace Unity.WebRTC),
// which Unity's Package Manager installs from Packages/manifest.json. NOTE: inside the BEERLabs.ProjectEsky.Networking.WebRTC
// namespace the bare identifier "WebRTC" resolves to the Esky namespace, so never call com.unity.webrtc's static
// WebRTC class unqualified here; use global::Unity.WebRTC.WebRTC.* if it is ever needed.
using Unity.WebRTC;
using System.Threading.Tasks;
using UnityEngine.Events;
using BEERLabs.ProjectEsky.Networking;
using BEERLabs.ProjectEsky.Networking.WebAPI;
using UnityEngine.Networking;

namespace BEERLabs.ProjectEsky.Networking.WebRTC.Discovery{
    public class NetworkingUtils{
        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
    }    
    // [MIGRATION] CHANGED CLASS: this used to derive from Microsoft.MixedReality.WebRTC.Unity.Signaler, which supplied
    //   (a) the "PeerConnection" field and the protected "_nativePeer",
    //   (b) a main-thread work queue pumped by Update(), and
    //   (c) the abstract SendMessageAsync(SdpMessage / IceCandidate) methods.
    // com.unity.webrtc has no Signaler and no PeerConnection component, and its callbacks already run on the Unity main thread,
    // so none of that is needed. The class is kept (same name, same public members BytesReceived / SendBytes) so existing
    // references still compile; it is now a plain MonoBehaviour. Update() stays virtual so subclasses can still call base.Update().
    public class PackageManagerHookBehaviour : MonoBehaviour{
        public delegate void BytesReceivedDelegate(byte[] b);
        public BytesReceivedDelegate BytesReceived;
        public virtual void SendBytes(byte[] b){
            
        }
        protected virtual void Update(){
        }
    }
    
    // [MIGRATION] IceMessageWebsocket, SdpMessageWebsocket and WebrtcShakeClass were MOVED UNCHANGED to
    // WebRTCCore/EskyPeerConnection.cs (assembly BEERLabs.ProjectEsky.Networking.WebRTC.Core, same namespace).
    public delegate void CreateOfferDelegate();
    public delegate void CreateResponseDelegate();

    public class WebRTCAutoDiscoveryHandler : PackageManagerHookBehaviour
    {
        
       
        [HideInInspector]
        public bool isAdding = false;
        public Dictionary<string, float> ClientsDiscovered = new Dictionary<string, float>();
        [HideInInspector]
        public List<string> cancellationsToSend = new List<string>();
        float internalHearbeatRate = 0f;
        float internalTimeoutRate = 0f;
        [Range(1.0f, 10f)]
        public float TimeoutBeforeReactivatingDiscovery = 5f;//5 second timeout before re-activating auto discovery
        [Range(0.0f,9.0f)]
        public float HeartbeatRate = 2f;

        public int ClientsFound = 0;
        public bool isHosting = false;
        [Range(0,5000)]
        public int TimeMSBetweenChecks;
        public bool isConnected = false;
        
        [Range(0.0f,5f)]
        public float TimeoutBeforeRemovalFromList = 3f;

        // [MIGRATION] NEW serialized settings. They replace what used to be configured on the child "PeerConnection" component of the
        // WebRTCConnectionManager prefab (IceServers = [STUN stun.l.google.com:19302], IceUsername, IceCredential) and the
        // PeerConnection.OnInitialized -> StartMyConnection UnityEvent wiring (now done by autoStartConnection).
        [Header("WebRTC (com.unity.webrtc)")]
        public bool autoStartConnection = true;
        public string[] IceServerUrls = new string[]{"stun:stun.l.google.com:19302"};
        public string IceUsername = "";
        public string IceCredential = "";

        public static WebRTCAutoDiscoveryHandler instance;
        BackgroundWorker objWorkerDiscovery;
        AutoDiscoverySender ads;
        AutoDiscoveryReceiver adr;
        public bool LogInfo;
        
        [HideInInspector]
        public string HostingIP = "";
        public List<byte[]> messagesQueue = new List<byte[]>();
        public UnityEvent<byte[]> onDataReceivedFromDataTrack;
        public UnityEvent onConnectionHandled;
        public UnityEvent onConnectionDropped;
        public bool hasMessagePrepared = false;
        public WebrtcShakeClass shake = new WebrtcShakeClass();
        bool createOffer = false;
        bool receiveAnswer = false;
        bool receiveOffer = false;
        bool initConnection = false;
        bool discConnection = false;
        bool currentlyConnected = false;
        // [MIGRATION] CHANGED: these held Microsoft SdpMessage objects ({Type, Content}). com.unity.webrtc takes the type and the
        // SDP text separately (RTCSessionDescription), and the type is implied by which field is used, so plain strings are enough.
        string sdpOffer = null;
        string sdpAnswer = null;
        // [MIGRATION] NEW: the ICE candidates that arrived together with the remote offer / answer. They are handed to the session
        // together with the SDP so they cannot be lost if the session is replaced between receiving the HTTP message and handling it.
        List<IceMessageWebsocket> sdpOfferIce = new List<IceMessageWebsocket>();
        List<IceMessageWebsocket> sdpAnswerIce = new List<IceMessageWebsocket>();
        // [MIGRATION] NEW: replaces the "PeerConnection" component that used to be referenced by the Signaler base class.
        EskyPeerConnection session = null;
        bool sessionResetRequested = false;
        bool myConnectionStarted = false;
        [HideInInspector]
        public int connected = 0;
        public void Awake(){
            
        }
        public void ReceiveHeartbeat(){
            if(isConnected){
                Debug.Log("Heartbeat Received");
                internalTimeoutRate = 0f;
            }else{
                Debug.Log("Heartbeat received at invalid times stopping the discovery anyways?");
                internalTimeoutRate = 0f;                
                isConnected = true;
                if(adr != null){
                    adr.Stop();
                }
            }
        }
        public void StopDiscoveryAnswering(){
            if(!isConnected){
                Debug.Log("Stopping Discovery from Answering");
                isConnected = true;
                if(adr != null){
                    adr.Stop();
                }
            }else{
                Debug.Log("We are already connected");
            }
        }
        // [MIGRATION] CHANGED: Start() now creates the WebRTC session. Previously the child PeerConnection component initialised itself in
        // OnEnable() and its OnInitialized UnityEvent (wired in the prefab / scene) called StartMyConnection(). The event no longer exists,
        // so StartMyConnection() is invoked from here one frame later (the old initialisation was asynchronous, so other scripts had
        // already run Start(), e.g. to set isHosting). Set autoStartConnection=false to call StartMyConnection() yourself.
        public void Start() {
            instance = this;
            CreateSession();
            if(autoStartConnection){
                StartCoroutine(StartMyConnectionNextFrame());
            }
        }
        IEnumerator StartMyConnectionNextFrame(){
            yield return null;
            StartMyConnection();
        }
        // [MIGRATION] CHANGED: now idempotent. The old UnityEvent fired it exactly once; an accidental second call (e.g. a leftover
        // persistent listener plus autoStartConnection) would subscribe the web events twice.
        public void StartMyConnection(){
            if(myConnectionStarted){
                Debug.Log("Connection already started");
                return;
            }
            myConnectionStarted = true;
            Debug.Log("Starting my Connection");
            WebAPIInterface.instance.SubscribeWebEvent("StopDiscovery",StopDiscoveryAnswering);
            StartReceiver();
            if(isHosting){
                StartSender();
            }else{
                WebAPIInterface.instance.SubscribeWebEvent("Heartbeat",ReceiveHeartbeat);
            }
            WebAPIInterface.instance.SubscribeEvent(HandleRequest);
        }
        void StartReceiver(){
            objWorkerDiscovery = new BackgroundWorker();
            objWorkerDiscovery.WorkerReportsProgress = true;
            objWorkerDiscovery.WorkerSupportsCancellation = true;
            adr = new AutoDiscoveryReceiver(ref objWorkerDiscovery,this,CreateOffer);
            objWorkerDiscovery.DoWork += new DoWorkEventHandler(adr.Start);
            objWorkerDiscovery.ProgressChanged += new ProgressChangedEventHandler(LogProgressChanged);
            objWorkerDiscovery.RunWorkerAsync();
        }
        public void StartSender(){
            isHosting = true;
            Debug.Log("Starting Sender");
            shake.iceMessages.Clear();
            if(objWorkerDiscovery != null){
                objWorkerDiscovery.CancelAsync();
            }
            if(adr != null){
                adr.Stop();
                adr = null;
            }
            objWorkerDiscovery = new BackgroundWorker();
            objWorkerDiscovery.WorkerReportsProgress = true;
            objWorkerDiscovery.WorkerSupportsCancellation = true;
            ads = new AutoDiscoverySender(ref objWorkerDiscovery,this);
            objWorkerDiscovery.DoWork += new DoWorkEventHandler(ads.Start);
            objWorkerDiscovery.ProgressChanged += new ProgressChangedEventHandler(LogProgressChanged);
            objWorkerDiscovery.RunWorkerAsync();          
        }
        // [MIGRATION] CHANGED FUNCTION: the three WebRTC branches no longer call the Task-based Microsoft API
        // (PeerConnection.StartConnection / HandleConnectionMessageAsync(...).ContinueWith / _nativePeer.CreateAnswer).
        // They start the equivalent coroutines on the EskyPeerConnection session. Everything else is unchanged:
        // the flag-pump pattern (flags set by the HTTP / UDP threads, executed here), heartbeat and connect/drop events.
        // Added: (1) a session reset when the previous session failed or was disconnected, so the next discovery round starts
        // from a fresh peer (a closed/failed RTCPeerConnection cannot be renegotiated); this is processed BEFORE the offer/answer
        // flags so a new offer is never applied to a dead session.
        private void FixedUpdate() {
            ProcessHeartBeat();
            if(sessionResetRequested){sessionResetRequested = false; ResetSession();}
            if(createOffer){createOffer = false; StartOffer(); Debug.Log("Starting Webrtc connection");}     
            if(receiveOffer){receiveOffer = false;
                StartCoroutine(HandleReceivedOffer(sdpOffer, sdpOfferIce));
            }
            if(receiveAnswer){receiveAnswer = false;
                connected += 1;
                StartCoroutine(HandleReceivedAnswer(sdpAnswer, sdpAnswerIce));
            }
            if(initConnection){initConnection = false;}
            if(discConnection){discConnection = false; ResetSession();}
            if(currentlyConnected != isConnected){
                currentlyConnected = isConnected;
                if(currentlyConnected){
                    onConnectionHandled.Invoke();
                }
                else{
                    onConnectionDropped.Invoke();
                }
            }
        }
        // [MIGRATION] NEW: creates (or re-creates) the WebRTC session. Replaces the PeerConnection component's InitializeAsync().
        // Local ICE candidates are collected into "shake" exactly as before; it is cleared here so a new session never sends
        // candidates that belong to a previous one.
        void CreateSession(){
            if(shake == null){ shake = new WebrtcShakeClass(); }
            if(shake.iceMessages == null){ shake.iceMessages = new List<IceMessageWebsocket>(); }
            else{ shake.iceMessages.Clear(); }
            session = new EskyPeerConnection(IceServerUrls, IceUsername, IceCredential);
            session.LocalIceCandidateReady += OnIceCandidateReadyToSend;
            session.LocalOfferReady += OnSdpOfferReadyToSend;
            session.LocalAnswerReady += OnSdpAnswerReadyToSend;
            session.MessageReceived += (channelId, bytes) => ReceiveMessageData(bytes);
            session.ChannelStateChanged += (channelId, state) => DataChannelOpen();
            session.OfferApplied += () => { Debug.Log("Handled offer"); waitingToCreateAnswer = true; };
            session.ConnectionStateChanged += OnPeerConnectionStateChanged;
            OnPeerInitialized();
        }
        // [MIGRATION] NEW: tears the current session down and starts a fresh one (see FixedUpdate).
        void ResetSession(){
            if(session != null){
                session.Dispose();
                session = null;
            }
            knownDataChannels.Clear();
            CreateSession();
        }
        // [MIGRATION] NEW: a failed ICE/DTLS connection behaves like the control channel closing: the connection is dropped and the
        // session is replaced on the next FixedUpdate. Transient "Disconnected" is ignored (the Esky heartbeat timeout handles it).
        void OnPeerConnectionStateChanged(RTCPeerConnectionState state){
            if(state == RTCPeerConnectionState.Failed){
                discConnection = true;
                isConnected = false;
            }
        }
        // [MIGRATION] NEW: replaces PeerConnection.StartConnection().
        void StartOffer(){
            if(session == null){ CreateSession(); }
            StartCoroutine(session.CreateOffer());
        }
        // [MIGRATION] NEW: replaces HandleConnectionMessageAsync(offer).ContinueWith(_ => { waitingToCreateAnswer = true; CreateAnswer(); }).
        IEnumerator HandleReceivedOffer(string offerSdp, List<IceMessageWebsocket> remoteIce){
            if(session == null){ CreateSession(); }
            yield return session.HandleOffer(offerSdp, remoteIce);
        }
        // [MIGRATION] NEW: replaces HandleConnectionMessageAsync(answer).
        IEnumerator HandleReceivedAnswer(string answerSdp, List<IceMessageWebsocket> remoteIce){
            if(session == null){ CreateSession(); }
            yield return session.HandleAnswer(answerSdp, remoteIce);
        }
        public IEnumerator SendHeartbeat(){            
        //    Debug.Log("Sending Heartbeat");
            List<string> keysToRemove = new List<string>();
            Dictionary<string,float> clients = new Dictionary<string, float>(ClientsDiscovered);
            foreach(KeyValuePair<string,float> clientConnected in clients){
      //          Debug.Log("Sending Heartbeat to: " + clientConnected.Key);
                WWWForm form = new WWWForm();
                form.AddField("APIType","Base");
                form.AddField("EventID","Heartbeat");                
                UnityWebRequest request = UnityWebRequest.Post("http://"+clientConnected.Key+":8079/",form);
                yield return request.SendWebRequest();
//                Debug.Log("Done Sending Heartbeat to: " + clientConnected.Key);
                if(request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError) {
  //          		Debug.Log("Issue sending heartbeat, remove from list client: " + clientConnected.Key);
                    keysToRemove.Add(clientConnected.Key);
                }                
                else {
    //                Debug.Log("Done Sending Heartbeat");
                }
            }
            while(keysToRemove.Count > 0){
                ClientsDiscovered.Remove(keysToRemove[0]);
                keysToRemove.RemoveAt(0);
            }
            
        }

        [HideInInspector]
        public bool waitingToCreateAnswer = false;
        [HideInInspector]        
        public bool createdAnswer = false;
        public IEnumerator CompleteConnection(string IP){
            WWWForm form = new WWWForm();
            form.AddField("APIType","Base");
            form.AddField("EventID","StopDiscovery");
            UnityWebRequest request = UnityWebRequest.Post("http://"+IP+":8079/",form);
            Debug.Log("Posting complete connection http://"+IP+":8079/");
            yield return request.SendWebRequest();
            if(request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError) {
        		Debug.Log("Issue sending complete connect request, trying again: " + request.error);
                StartCoroutine(CompleteConnection(IP));
            }
            
            else {
                createOffer = true;
              Debug.Log("Done Sending Completion request");
            }

        }        
        void ProcessHeartBeat(){
            //we should rely on the webapi to do things related to cleanup, but for now letting the list accumulate is OK
            //iterate over each entry in the clients dictionary
            if(isHosting){
                if(ClientsFound > 0){
                    internalHearbeatRate += Time.fixedDeltaTime;
                    if(internalHearbeatRate > HeartbeatRate){
                        internalHearbeatRate = 0;
                        StartCoroutine(SendHeartbeat());
                    }
                }
            }else{
                if(isConnected){
                    internalTimeoutRate += Time.fixedDeltaTime;
                    if(internalTimeoutRate > TimeoutBeforeReactivatingDiscovery){
                       // Debug.Log("Heartbeat Timeout! Stopping the receiver");
                        isConnected = false;
                        StartReceiver();
                    }
                }
            }
            ClientsFound = ClientsDiscovered.Count;
        }
        // [MIGRATION] NOTE: unchanged body. base.Update() used to drain Signaler's main-thread work queue; it is now an empty virtual
        // in PackageManagerHookBehaviour (callbacks are already on the main thread).
        protected override void Update(){
            base.Update();
             while(cancellationsToSend.Count > 0){
                StartCoroutine(CompleteConnection(cancellationsToSend[0]));
                cancellationsToSend.RemoveAt(0);
            }            
            while(messagesQueue.Count > 0){
                byte[] b = messagesQueue[0];
                messagesQueue.RemoveAt(0);
                onDataReceivedFromDataTrack.Invoke(b);
            }
        }
        public void Finish(){
        }
        public void ReceivedConnection(){
        }
        public void StoppedConnection(){
                onConnectionDropped.Invoke();
        }
        public void ReceiveMessageData(byte[] b){   
            messagesQueue.Add(b); 
        }
        // [MIGRATION] CHANGED FUNCTION: was knownDataChannels[1].SendMessage(b) (Microsoft DataChannel). Still channel 1, still bytes;
        // now goes through EskyPeerConnection.Send, which returns false (instead of throwing) if the channel is not open.
        public override void SendBytes(byte[] b){
                if(session != null){ session.Send(EskyPeerConnection.UnreliableTransferChannelId, b); }
        }
        // [MIGRATION] CHANGED FUNCTION: see SendBytes. The original also used channel 1 here (channel 2 "reliabletransferChannel" was created
        // but never used); that is preserved on purpose. Both channels are ordered+reliable anyway.
        public void SendBytesReliable(byte[] b){
                if(session != null){ session.Send(EskyPeerConnection.UnreliableTransferChannelId, b); }
        }
        public void Disconnect(){
                discConnection = true;
                isConnected = false;                
        }
        // [MIGRATION] CHANGED: value type is now com.unity.webrtc's RTCDataChannel (was Microsoft DataChannel). Filled by OnPeerInitialized().
        public Dictionary<int,RTCDataChannel> knownDataChannels = new Dictionary<int, RTCDataChannel>();
        // [MIGRATION] CHANGED FUNCTION: kept as a public entry point for external callers that create their own channel. Parameter type is
        // RTCDataChannel; MessageReceived/StateChanged events became the OnMessage/OnOpen/OnClose delegates, channel.ID became channel.Id.
        // The Esky channels themselves are wired inside EskyPeerConnection and do not go through this method.
        public void DataChannelAddedDelegate(RTCDataChannel channel){
            channel.OnMessage = ReceiveMessageData;
            channel.OnOpen = DataChannelOpen;
            channel.OnClose = DataChannelOpen;
            knownDataChannels[channel.Id] = channel;
        }
        // [MIGRATION] CHANGED FUNCTION: logic identical (channel 0 Open => connected, Closed => disconnected). The state is now read from
        // RTCDataChannel.ReadyState / RTCDataChannelState instead of DataChannel.State / ChannelState, via the session (safe if the session was replaced).
        public void DataChannelOpen(){
            if(session == null){ return; }
            switch(session.GetChannelState(EskyPeerConnection.MessageTransferChannelId)){
                case RTCDataChannelState.Open:
                initConnection = true;                
                isConnected = true;
                break;
                case RTCDataChannelState.Closed:
                discConnection = true;
                isConnected = false;
                break;
            } 
        }
        // [MIGRATION] CHANGED FUNCTION: used to be "override" of Signaler.OnPeerInitialized (fired by the PeerConnection component) and to call
        // PeerConnection.Peer.AddDataChannelAsync three times with ContinueWith error rethrow. It is now called by CreateSession() and creates the
        // same three channels (ids 0,1,2; labels message_transfer / unreliabletransferChannel / reliabletransferChannel; ordered and reliable,
        // pre-negotiated) synchronously; errors surface as exceptions instead of faulted Tasks.
        public virtual void OnPeerInitialized(){ 
            session.CreateDataChannels();
            knownDataChannels.Clear();
            for(int i = 0; i < EskyPeerConnection.DataChannelCount; i++){
                RTCDataChannel channel = session.GetChannel(i);
                if(channel != null){ knownDataChannels[i] = channel; }
            }
        }
        
        private void LogProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            // Report thread messages to Console
            if(LogInfo)
            Debug.Log(e.UserState.ToString());
        }

        // [MIGRATION] CHANGED FUNCTION: additionally disposes the WebRTC session (the Microsoft PeerConnection component did this in its own
        // OnDisable/OnDestroy; the native peer and data channels of com.unity.webrtc must be released explicitly).
        public void OnDestroy(){
            if(session != null){
                session.Dispose();
                session = null;
            }
            if(ads != null){
                ads.Stop();
            }
            if(adr != null){

                adr.Stop();
            }
        }
        
        public void CreateOffer(){
            createOffer = true;
        }


        // [MIGRATION] CHANGED FUNCTION: no longer builds a Microsoft SdpMessage{Type=Offer,Content=sdp}; stores the SDP text and a copy of the
        // candidates. ICE is no longer applied immediately (see ReceiveIceCandidate): it is applied by the session once the remote description is set.
        public void ReceiveCompletedOffer(WebrtcShakeClass receivedClass){
            sdpOfferIce = receivedClass.iceMessages != null ? new List<IceMessageWebsocket>(receivedClass.iceMessages) : new List<IceMessageWebsocket>();
            sdpOffer = receivedClass.sdpMessage.sdp;
            receiveOffer = true;
        }
        // [MIGRATION] CHANGED FUNCTION: same as ReceiveCompletedOffer, for the answer.
        public void ReceiveCompletedAnswer(WebrtcShakeClass receivedClass){
            sdpAnswerIce = receivedClass.iceMessages != null ? new List<IceMessageWebsocket>(receivedClass.iceMessages) : new List<IceMessageWebsocket>();
            sdpAnswer = receivedClass.sdpMessage.sdp;
            receiveAnswer = true;
        }
        // [MIGRATION] CHANGED FUNCTION: was _nativePeer.AddIceCandidate(new IceCandidate{SdpMid,SdpMlineIndex,Content}) per entry. Now forwards to the
        // session, which queues candidates until a remote description exists (libwebrtc rejects them before that).
        public void ReceiveIceCandidate(WebrtcShakeClass receivedClass){
            if(session == null || receivedClass.iceMessages == null){ return; }
            session.AddRemoteIceCandidates(receivedClass.iceMessages);
        }
        
        // [MIGRATION] CHANGED FUNCTION: was "protected override void OnIceCandidateReadyToSend(IceCandidate candidate)" of Signaler. The session now
        // delivers a ready-made IceMessageWebsocket (candidate text / sdpMlineIndex / sdpMid), so the body only appends it to the outgoing bundle.
        protected virtual void OnIceCandidateReadyToSend(IceMessageWebsocket imws)
        {
            shake.iceMessages.Add(imws);
        }
        // [MIGRATION] CHANGED FUNCTION: was "protected override void OnSdpOfferReadyToSend(SdpMessage offer)" using offer.Content; now takes the SDP text.
        protected virtual void OnSdpOfferReadyToSend(string offerSdp)
        {

            SdpMessageWebsocket sdpws = new SdpMessageWebsocket(offerSdp,"offer");
            shake.sdpMessage = sdpws;
            StartCoroutine(SendSDPOffer());
        }
        // [MIGRATION] CHANGED FUNCTION: same as OnSdpOfferReadyToSend, for the answer.
        protected virtual void OnSdpAnswerReadyToSend(string answerSdp)
        {

            SdpMessageWebsocket sdpws = new SdpMessageWebsocket(answerSdp,"answer");
            shake.sdpMessage = sdpws;    
            StartCoroutine(SendSDPAnswer());

        }
        public bool HandleRequest(Request request, Response response){
            string key = request.formData["EventID"].Value.Trim();
            

            switch(key){
                case "SDPOffer":
                Debug.Log("Checking: " + key + "," + request.formData["Offer"].Value);                
                // [MIGRATION] CHANGED: the parsed message is no longer assigned to "shake". "shake" is the OUTGOING bundle (local SDP + local
                // ICE); overwriting it with the remote bundle made the answer re-send the offerer's own candidates back to it.
                ReceiveCompletedOffer(JsonUtility.FromJson<WebrtcShakeClass>(request.formData["Offer"].Value));
              //  receiveOffer = true;                
                response.statusCode = 200;
                response.message = "OK";
                response.Write(request.uri.LocalPath + " OK");                

                return true;
                case "SDPAnswer":
  //              Debug.Log("Checking: " + key + "," + request.formData["Answer"].Value);                
                // [MIGRATION] CHANGED: see "SDPOffer" above.
                ReceiveCompletedAnswer(JsonUtility.FromJson<WebrtcShakeClass>(request.formData["Answer"].Value));                
              //  receiveAnswer = true;                
                response.statusCode = 200;
                response.message = "OK";
                response.Write(request.uri.LocalPath + " OK");                
                return true;

            }
            return false;
        }
        public IEnumerator SendSDPOffer(){
            while(shake.iceMessages.Count == 0){
                yield return new WaitForSeconds(1);
            }
            JSONRequest jsonreq = new JSONRequest();
            string offer = JsonUtility.ToJson(shake);            
            jsonreq.ModifyRequest("EventID","SDPOffer");
            jsonreq.ModifyRequest("Offer",offer);            
            Dictionary<string, float> clientsToSend = new Dictionary<string, float>(ClientsDiscovered);
            foreach(KeyValuePair<string,float> client in clientsToSend){
                string location = "http://"+client.Key+":"+WebAPIInterface.instance.port+"/";
                Debug.Log("Sending SDP Offer to: " + location);
                UnityWebRequest request = UnityWebRequest.Get(location);
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(jsonreq)));
                request.uploadHandler.contentType = "application/json";
                request.SetRequestHeader("Content-Type","application/json");
                yield return request.SendWebRequest();     
                if(request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError) {
    //                Debug.Log("Issue sending offer to: " + location);
                }                
                else {
    //              Debug.Log("Done Sending offer to: " + location);            
                } 
                yield return null;
            }
        }
        public IEnumerator SendSDPAnswer(){
            JSONRequest jsonreq = new JSONRequest();
            string offer = JsonUtility.ToJson(shake);            
            jsonreq.ModifyRequest("EventID","SDPAnswer");
            jsonreq.ModifyRequest("Answer",offer);                         
            string location = "http://"+HostingIP+":"+WebAPIInterface.instance.port+"/";
            UnityWebRequest request = UnityWebRequest.Post(location,JsonUtility.ToJson(jsonreq));
            request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(jsonreq)));
            request.uploadHandler.contentType = "application/json";                 
            yield return request.SendWebRequest();
            if(request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError) {
                //  Debug.Log("Issue sending Answer to: " + location);
            }                
            else {
                //Debug.Log("Done Sending Answer to: " + location);            
            }
            yield return null;            

        }
    }
    public class AutoDiscoveryReceiver
    {
        public CreateOfferDelegate createOfferDelegate;
        public WebRTCAutoDiscoveryHandler hookedAutoDiscovery;
        private System.ComponentModel.BackgroundWorker workerUDP;

        // Port the UDP server will listen to broadcast packets from UDP Clients.
        private int AutoDiscoveryPort = 18500;

        // Sample byte sequency that Identify a Server Address Request. You may change on the client-side also.
        // Implementing other byte sequences for other actions are also valid. You as developer may know that ;)
        byte[] packetBytes = new byte[] { 0x1, 0x2, 0x3 };

        // In the following example code we reply to incoming client an IP Address that
        // Client must use as server for any purpose. (TCP Server not implemented)
        public IPAddress addrDaemonListenIP;
        
        // Which port we will broadcast as TCP Server (not implemented).
        public int BroadCastDaemonPort = 0;

        private bool disposing = false;

        public AutoDiscoveryReceiver(ref BackgroundWorker workerUDP, WebRTCAutoDiscoveryHandler hookedDiscovery, CreateOfferDelegate offerDelegate)
        {
            this.workerUDP = workerUDP;
            this.BroadCastDaemonPort = AutoDiscoveryPort;
            this.addrDaemonListenIP = IPAddress.Parse("0.0.0.0");
            this.hookedAutoDiscovery = hookedDiscovery;
            createOfferDelegate = offerDelegate;
        }

        public void Stop()
        {
            workerUDP.CancelAsync();
            workerUDP.Dispose();
            this.disposing = true;
        }

        /// <summary>
        /// Start the listener.
        /// </summary>
        public void Start(object sender, DoWorkEventArgs e)
        {
            try
            {
//                this.workerUDP.ReportProgress(30, "AutoDiscoveryReceiver::Service Listening " + this.AutoDiscoveryPort + "/UDP");
                byte[] ReceivedData;


                // Local End-Point
                IPEndPoint LocalEP = new IPEndPoint(IPAddress.Any, AutoDiscoveryPort);
                this.workerUDP.ReportProgress(30, "AutoDiscoveryReceiver::Service Listening " +NetworkingUtils.GetLocalIPAddress()+ ":" + this.AutoDiscoveryPort + "/UDP");
                
                IPEndPoint RemoteEP = new IPEndPoint(IPAddress.Any, 0);
                UdpClient newsock = new UdpClient(LocalEP);
                ReceivedData = newsock.Receive(ref RemoteEP);
                IPEndPoint IncomingIP = (IPEndPoint)RemoteEP;
                while (!disposing)
                {
                        if (ReceivedData.SequenceEqual(packetBytes))
                        {
                            // Use ReportProgress from BackgroundWorker as communication channel between main app and the worker thread.
                            this.workerUDP.ReportProgress(1, "Discovery from " + IncomingIP + "/UDP");
                            // Here we reply the Service IP and Port (TCP).. 
                            // You must point to your server and service port. For example a webserver: sending the correct IP and port 80.
                            this.workerUDP.ReportProgress(1, "Got discovered, sending response: bleh");
                            WebRTCAutoDiscoveryHandler.instance.HostingIP = IncomingIP.Address.ToString();
                            this.workerUDP.ReportProgress(1,"Got response from IP: " + IncomingIP.Address.ToString());
                            byte[] packetBytesAck = Encoding.Unicode.GetBytes("ACK*"+NetworkingUtils.GetLocalIPAddress()); // Acknowledged
                            newsock.Send(packetBytesAck, packetBytesAck.Length, RemoteEP);
                            this.workerUDP.ReportProgress(1, "Answering(ACK) " + packetBytesAck.Length + " bytes to " + IncomingIP);
                            //createOfferDelegate.Invoke();
                        }
                        else
                        {
                            // Unknown packet type.
                            this.workerUDP.ReportProgress(1, "Answering(NAK) " + packetBytes.Length + " bytes to " + IncomingIP);
                            byte[] packetBytesNak = Encoding.Unicode.GetBytes("NAK"); // Not Acknowledged
                            newsock.Send(packetBytesNak, packetBytesNak.Length, RemoteEP);
                        }
                        ReceivedData = newsock.Receive(ref RemoteEP);
                }

                

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

        }
    } 
    public class AutoDiscoverySender
        {
            public WebRTCAutoDiscoveryHandler hookedAutoDiscovery;
            public bool disposing = false;

            // Fixed Port for broadcast.
            // You may change it but CLIENT and SERVER must be configured with the same port.
            private int AutoDiscoveryPort = 18500;
            
            private int AutoDiscoveryTimeout = 4000;

            // Sample byte sequency that Identify a Server Address Request. You may change on the client-side also.
            // Implementing other byte sequences for other actions are also valid. You as developer may know that ;)
            byte[] packetBytes = new byte[] { 0x1, 0x2, 0x3 };

            // Do not change:
            // We will set this variable when Auto Discovery server reply with ACK;
            // Do not change. This will be set if Server is found and check for TCP Connections is OK
            public string ServerAddress = String.Empty;
            public int ServerPort = 0;

            private BackgroundWorker worker;

            public AutoDiscoverySender(ref BackgroundWorker worker, WebRTCAutoDiscoveryHandler hookedDiscovery)
            {
                this.worker = worker;
                worker.ReportProgress(1, "AutoDiscoverySender::Started at " + AutoDiscoveryPort + "/UDP");
                this.hookedAutoDiscovery = hookedDiscovery;
            }

            public void Start(object sender, DoWorkEventArgs e)
            {
                try
                {
                    while (this.disposing == false)
                    {
                        // Must look for server.. Repeat until configured.
                            if (ServerAddress == String.Empty)
                            {
                                this.worker.ReportProgress(2, "AutoDiscovery::Looking for server..");
                                // Broadcast the query
                                sendBroadcastSearchPacket();
                            }
                            Thread.Sleep(hookedAutoDiscovery.TimeMSBetweenChecks);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            public void Stop()
            {
                this.disposing = true;
            }

            // Here is to check if the server replied by Auto Discovery is Alive.
            private bool sendBroadcastSearchPacket()
            {

                bool returnVal = false;
                UdpClient udp = new UdpClient();
                udp.EnableBroadcast = true;

                udp.Client.ReceiveTimeout = AutoDiscoveryTimeout;

                IPEndPoint groupEP = new IPEndPoint(IPAddress.Parse("255.255.255.255"), AutoDiscoveryPort);


                try
                {
                    udp.Send(packetBytes, packetBytes.Length, groupEP);                    
                    byte[] receiveBytes = udp.Receive(ref groupEP);                    
                    string returnData = Encoding.Unicode.GetString(receiveBytes, 0, receiveBytes.Length);
//                    this.worker.ReportProgress(3,"Received Data: " + returnData);   
                    string[] returnDataSpl = returnData.Split('*');
                    if (returnDataSpl[0] == "NAK")
                    {
  //                      this.worker.ReportProgress(3, "AutoDiscovery::INVALID REQUEST");
                    }else if(returnDataSpl[0] == "ACK"){//everything ok
                        this.worker.ReportProgress(3,"It was ACK! " + returnDataSpl[1]);
    //                    Debug.Log("Received IP Address: " + returnDataSpl[1]);
                        hookedAutoDiscovery.ClientsDiscovered[returnDataSpl[1]] = hookedAutoDiscovery.TimeoutBeforeRemovalFromList;                             
                        hookedAutoDiscovery.cancellationsToSend.Add(returnDataSpl[1]);
                        hookedAutoDiscovery.CreateOffer();
                    }else{
                        this.worker.ReportProgress(3,"RECEIVED GARBAGE?");   
                    }
                }
                catch (SocketException e)
                {
                    this.worker.ReportProgress(1, "AutoDiscovery::Couldn't Find New Peers, trying again");

                }
                udp.Close();
                return (returnVal);

            }

        }


}