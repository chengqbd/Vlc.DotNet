﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Vlc.DotNet.Core.Interops;
using Vlc.DotNet.Core.Interops.Signatures;

namespace Vlc.DotNet.Core
{
    public sealed partial class VlcMediaPlayer : IDisposable
    {
        private VlcMediaPlayerInstance myMediaPlayerInstance;

        public VlcMediaPlayer(DirectoryInfo vlcLibDirectory)
            : this(VlcManager.GetInstance(vlcLibDirectory))
        {
        }

        public VlcMediaPlayer(DirectoryInfo vlcLibDirectory, string[] options)
            : this(VlcManager.GetInstance(vlcLibDirectory), options)
        {
        }

        internal VlcMediaPlayer(VlcManager manager)
        {
            Manager = manager;
#if DEBUG
            Manager.CreateNewInstance(new[]
            {
                "--extraintf=logger",
                "--verbose=2"
            });
#else
            Manager.CreateNewInstance(new[]
            {
                "--quiet"
            });
#endif
            myMediaPlayerInstance = manager.CreateMediaPlayer();
            RegisterEvents();
            Chapters = new ChapterManagement(manager, myMediaPlayerInstance);
            SubTitles = new SubTitlesManagement(manager, myMediaPlayerInstance);
            Video = new VideoManagement(manager, myMediaPlayerInstance);
            Audio = new AudioManagement(manager, myMediaPlayerInstance);
#if !NET20 && !NET35 && !NET40
            Dialogs = new DialogsManagement(manager, myMediaPlayerInstance);
#endif
        }

        internal VlcMediaPlayer(VlcManager manager, string[] options)
        {
            Manager = manager;
            Manager.CreateNewInstance(options);

            myMediaPlayerInstance = manager.CreateMediaPlayer();
            RegisterEvents();
            Chapters = new ChapterManagement(manager, myMediaPlayerInstance);
            SubTitles = new SubTitlesManagement(manager, myMediaPlayerInstance);
            Video = new VideoManagement(manager, myMediaPlayerInstance);
            Audio = new AudioManagement(manager, myMediaPlayerInstance);
#if !NET20 && !NET35 && !NET40
            Dialogs = new DialogsManagement(manager, myMediaPlayerInstance);
#endif
        }

        /// <summary>
        /// WARNING : USE AT YOUR OWN RISK!
        /// Gets the low-level interop manager that calls the methods on the libvlc library.
        /// This is useful if a higher-level API is missing.
        /// </summary>
        public VlcManager Manager { get; private set; }

        public IntPtr VideoHostControlHandle
        {
            get { return Manager.GetMediaPlayerVideoHostHandle(myMediaPlayerInstance); }
            set { Manager.SetMediaPlayerVideoHostHandle(myMediaPlayerInstance, value); }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (myMediaPlayerInstance == IntPtr.Zero)
                return;
            UnregisterEvents();
            if (IsPlaying())
                Stop();

            if (VlcMedia.LoadedMedias.ContainsKey(this))
            foreach (var loadedMedia in VlcMedia.LoadedMedias[this])
            {
                loadedMedia.Dispose();
            }
            VlcMedia.LoadedMedias.Remove(this);

            myMediaPlayerInstance.Dispose();
            Manager.Dispose();
        }

        ~VlcMediaPlayer()
        {
            Dispose(false);
        }

        public VlcMedia SetMedia(FileInfo file, params string[] options)
        {
            return SetMedia(new VlcMedia(this, file, options));
        }

        public VlcMedia SetMedia(Uri uri, params string[] options)
        {
            return SetMedia(new VlcMedia(this, uri, options));
        }

        public VlcMedia SetMedia(string mrl, params string[] options)
        {
            return SetMedia(new VlcMedia(this, mrl, options));
        }

        public VlcMedia SetMedia(Stream stream, params string[] options)
        {
            return SetMedia(new VlcMedia(this, stream, options));
        }

        private VlcMedia SetMedia(VlcMedia media)
        {
            var currentMedia = GetMedia();
            if (currentMedia != null && currentMedia.MediaInstance != media.MediaInstance)
                currentMedia.Dispose();
            Manager.SetMediaToMediaPlayer(myMediaPlayerInstance, media.MediaInstance);
            return media;
        }

        public VlcMedia GetMedia()
        {
            var mediaPtr = Manager.GetMediaFromMediaPlayer(myMediaPlayerInstance);
            if (mediaPtr.Pointer != IntPtr.Zero)
                return new VlcMedia(this, mediaPtr);
            return null;
        }

        public void Play()
        {
            Manager.Play(myMediaPlayerInstance);
        }

        /// <summary>
        /// Overload, provided for convenience that calls <see cref="SetMedia(System.IO.FileInfo,string[])"/> before <see cref="Play()"/>
        /// </summary>
        /// <param name="file">The file to play</param>
        /// <param name="options">The options to be given</param>
        public void Play(FileInfo file, params string[] options)
        {
            this.SetMedia(file, options);
            this.Play();
        }

        /// <summary>
        /// Overload, provided for convenience that calls <see cref="SetMedia(System.Uri,string[])"/> before <see cref="Play()"/>
        /// </summary>
        /// <param name="uri">The uri to play</param>
        /// <param name="options">The options to be given</param>
        public void Play(Uri uri, params string[] options)
        {
            this.SetMedia(uri, options);
            this.Play();
        }

        /// <summary>
        /// Overload, provided for convenience that calls <see cref="SetMedia(string,string[])"/> before <see cref="Play()"/>
        /// </summary>
        /// <param name="mrl">The mrl to play</param>
        /// <param name="options">The options to be given</param>
        public void Play(string mrl, params string[] options)
        {
            this.SetMedia(mrl, options);
            this.Play();
        }

        /// <summary>
        /// Overload, provided for convenience that calls <see cref="SetMedia(System.IO.Stream,string[])"/> before <see cref="Play()"/>
        /// </summary>
        /// <param name="stream">The stream to play</param>
        /// <param name="options">The options to be given</param>
        public void Play(Stream stream, params string[] options)
        {
            this.SetMedia(stream, options);
            this.Play();
        }

        public void Pause()
        {
            Manager.Pause(myMediaPlayerInstance);
        }

        public void Stop()
        {
            Manager.Stop(myMediaPlayerInstance);
        }

        public bool IsPlaying()
        {
            return Manager.IsPlaying(myMediaPlayerInstance);
        }

        public bool IsPausable()
        {
            return Manager.IsPausable(myMediaPlayerInstance);
        }

        public void NextFrame()
        {
            Manager.NextFrame(myMediaPlayerInstance);
        }

        public IEnumerable<FilterModuleDescription> GetAudioFilters()
        {
            var module = Manager.GetAudioFilterList();
            ModuleDescriptionStructure nextModule = MarshalHelper.PtrToStructure<ModuleDescriptionStructure>(module);
            var result = GetSubFilter(nextModule);
            if (module != IntPtr.Zero)
                Manager.ReleaseModuleDescriptionInstance(module);
            return result;
        }

        private List<FilterModuleDescription> GetSubFilter(ModuleDescriptionStructure module)
        {
            var result = new List<FilterModuleDescription>();
            var filterModule = FilterModuleDescription.GetFilterModuleDescription(module);
            if (filterModule == null)
            {
                return result;
            }
            result.Add(filterModule);
            if (module.NextModule != IntPtr.Zero)
            {
                ModuleDescriptionStructure nextModule = MarshalHelper.PtrToStructure<ModuleDescriptionStructure>(module.NextModule);
                var data = GetSubFilter(nextModule);
                if (data.Count > 0)
                    result.AddRange(data);
            }
            return result;
        }

        public IEnumerable<FilterModuleDescription> GetVideoFilters()
        {
            var module = Manager.GetVideoFilterList();
            ModuleDescriptionStructure nextModule = MarshalHelper.PtrToStructure<ModuleDescriptionStructure>(module);
            var result = GetSubFilter(nextModule);
            if (module != IntPtr.Zero)
                Manager.ReleaseModuleDescriptionInstance(module);
            return result;
        }

        public float Position
        {
            get { return Manager.GetMediaPosition(myMediaPlayerInstance); }
            set { Manager.SetMediaPosition(myMediaPlayerInstance, value); }
        }

        public bool CouldPlay
        {
            get { return Manager.CouldPlay(myMediaPlayerInstance); }
        }

        public IChapterManagement Chapters { get; private set; }

        public float Rate
        {
            get { return Manager.GetRate(myMediaPlayerInstance); }
            set { Manager.SetRate(myMediaPlayerInstance, value); }
        }

        public MediaStates State
        {
            get { return Manager.GetMediaPlayerState(myMediaPlayerInstance); }
        }

        public float FramesPerSecond
        {
            get { return Manager.GetFramesPerSecond(myMediaPlayerInstance); }
        }

        public bool IsSeekable
        {
            get { return Manager.IsSeekable(myMediaPlayerInstance); }
        }

        public void Navigate(NavigateModes navigateMode)
        {
            Manager.Navigate(myMediaPlayerInstance, navigateMode);
        }

        public ISubTitlesManagement SubTitles { get;  }

        public IVideoManagement Video { get; }

        public IAudioManagement Audio { get; }

#if !NET20 && !NET35 && !NET40
        public IDialogsManagement Dialogs { get; }
#endif

        public long Length
        {
            get { return Manager.GetLength(myMediaPlayerInstance); }
        }

        public long Time
        {
            get { return Manager.GetTime(myMediaPlayerInstance); }
            set { Manager.SetTime(myMediaPlayerInstance, value); }
        }

        public int Spu
        {
            get { return Manager.GetVideoSpu(myMediaPlayerInstance); }
            set { Manager.SetVideoSpu(myMediaPlayerInstance, value); }
        }

        public bool TakeSnapshot(FileInfo file)
        {
            return TakeSnapshot(file, 0, 0);
        }

        public bool TakeSnapshot(FileInfo file, uint width, uint height)
        {
            return TakeSnapshot(0, file.FullName, width, height);
        }

        /// <summary>
        /// Take a snapshot of the current video window.
        /// </summary>
        /// <param name="outputNumber">The number of video output (typically 0 for the first/only one)</param>
        /// <param name="file">The path of a file or a folder to save the screenshot into</param>
        /// <param name="width">the snapshot's width</param>
        /// <param name="height">the snapshot's height</param>
        /// <returns>A boolean indicating whether the screenshot was sucessfully taken</returns>
        /// <remarks>
        /// If i_width AND i_height is 0, original size is used.
        /// If i_width XOR i_height is 0, original aspect-ratio is preserved.
        /// </remarks>
        public bool TakeSnapshot(uint outputNumber, string file, uint width, uint height)
        {
            return Manager.TakeSnapshot(myMediaPlayerInstance, outputNumber, file, width, height);
        }

        private void RegisterEvents()
        {
            var vlcEventManager = Manager.GetMediaPlayerEventManager(myMediaPlayerInstance);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerBackward, myOnMediaPlayerBackwardInternalEventCallback = OnMediaPlayerBackwardInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerBuffering, myOnMediaPlayerBufferingInternalEventCallback = OnMediaPlayerBufferingInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerEncounteredError, myOnMediaPlayerEncounteredErrorInternalEventCallback = OnMediaPlayerEncounteredErrorInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerEndReached, myOnMediaPlayerEndReachedInternalEventCallback = OnMediaPlayerEndReachedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerForward, myOnMediaPlayerForwardInternalEventCallback = OnMediaPlayerForwardInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerLengthChanged, myOnMediaPlayerLengthChangedInternalEventCallback = OnMediaPlayerLengthChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerMediaChanged, myOnMediaPlayerMediaChangedInternalEventCallback = OnMediaPlayerMediaChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerOpening, myOnMediaPlayerOpeningInternalEventCallback = OnMediaPlayerOpeningInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerPausableChanged, myOnMediaPlayerPausableChangedInternalEventCallback = OnMediaPlayerPausableChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerPaused, myOnMediaPlayerPausedInternalEventCallback = OnMediaPlayerPausedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerPlaying, myOnMediaPlayerPlayingInternalEventCallback = OnMediaPlayerPlayingInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerPositionChanged, myOnMediaPlayerPositionChangedInternalEventCallback = OnMediaPlayerPositionChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerScrambledChanged, myOnMediaPlayerScrambledChangedInternalEventCallback = OnMediaPlayerScrambledChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerSeekableChanged, myOnMediaPlayerSeekableChangedInternalEventCallback = OnMediaPlayerSeekableChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerSnapshotTaken, myOnMediaPlayerSnapshotTakenInternalEventCallback = OnMediaPlayerSnapshotTakenInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerStopped, myOnMediaPlayerStoppedInternalEventCallback = OnMediaPlayerStoppedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerTimeChanged, myOnMediaPlayerTimeChangedInternalEventCallback = OnMediaPlayerTimeChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerTitleChanged, myOnMediaPlayerTitleChangedInternalEventCallback = OnMediaPlayerTitleChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerVout, myOnMediaPlayerVideoOutChangedInternalEventCallback = OnMediaPlayerVideoOutChangedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerEsAdded, myOnMediaPlayerEsAddedInternalEventCallback = OnMediaPlayerEsAddedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerEsDeleted, myOnMediaPlayerEsDeletedInternalEventCallback = OnMediaPlayerEsDeletedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerEsSelected, myOnMediaPlayerEsSelectedInternalEventCallback = OnMediaPlayerEsSelectedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerCorked, myOnMediaPlayerCorkedInternalEventCallback = OnMediaPlayerCorkedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerUncorked, myOnMediaPlayerUncorkedInternalEventCallback = OnMediaPlayerUncorkedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerMuted, myOnMediaPlayerMutedInternalEventCallback = OnMediaPlayerMutedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerUnmuted, myOnMediaPlayerUnmutedInternalEventCallback = OnMediaPlayerUnmutedInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerAudioVolume, myOnMediaPlayerAudioVolumeInternalEventCallback = OnMediaPlayerAudioVolumeInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerAudioDevice, myOnMediaPlayerAudioDeviceInternalEventCallback = OnMediaPlayerAudioDeviceInternal);
            Manager.AttachEvent(vlcEventManager, EventTypes.MediaPlayerChapterChanged, myOnMediaPlayerChapterChangedInternalEventCallback = OnMediaPlayerChapterChangedInternal);
            vlcEventManager.Dispose();
        }

        private void UnregisterEvents()
        {
            var vlcEventManager = Manager.GetMediaPlayerEventManager(myMediaPlayerInstance);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerBackward, myOnMediaPlayerBackwardInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerBuffering, myOnMediaPlayerBufferingInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerEncounteredError, myOnMediaPlayerEncounteredErrorInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerEndReached, myOnMediaPlayerEndReachedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerForward, myOnMediaPlayerForwardInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerLengthChanged, myOnMediaPlayerLengthChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerMediaChanged, myOnMediaPlayerMediaChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerOpening, myOnMediaPlayerOpeningInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerPausableChanged, myOnMediaPlayerPausableChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerPaused, myOnMediaPlayerPausedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerPlaying, myOnMediaPlayerPlayingInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerPositionChanged, myOnMediaPlayerPositionChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerScrambledChanged, myOnMediaPlayerScrambledChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerSeekableChanged, myOnMediaPlayerSeekableChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerSnapshotTaken, myOnMediaPlayerSnapshotTakenInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerStopped, myOnMediaPlayerStoppedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerTimeChanged, myOnMediaPlayerTimeChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerTitleChanged, myOnMediaPlayerTitleChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerVout, myOnMediaPlayerVideoOutChangedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerEsAdded, myOnMediaPlayerEsAddedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerEsDeleted, myOnMediaPlayerEsDeletedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerEsSelected, myOnMediaPlayerEsSelectedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerCorked, myOnMediaPlayerCorkedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerUncorked, myOnMediaPlayerUncorkedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerMuted, myOnMediaPlayerMutedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerUnmuted, myOnMediaPlayerUnmutedInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerAudioVolume, myOnMediaPlayerAudioVolumeInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerAudioDevice, myOnMediaPlayerAudioDeviceInternalEventCallback);
            Manager.DetachEvent(vlcEventManager, EventTypes.MediaPlayerChapterChanged, myOnMediaPlayerChapterChangedInternalEventCallback);
            vlcEventManager.Dispose();
        }
    }
}
