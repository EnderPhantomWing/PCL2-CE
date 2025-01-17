﻿Imports TagLib
Imports Windows.Media
Imports Windows.Storage
Imports Windows.Storage.Pickers
Public Module ModMusic

#Region "播放列表"

    ''' <summary>
    ''' 接下来要播放的音乐文件路径。未初始化时为 Nothing。
    ''' </summary>
    Public MusicWaitingList As List(Of String) = Nothing
    ''' <summary>
    ''' 全部音乐文件路径。未初始化时为 Nothing。
    ''' </summary>
    Public MusicAllList As List(Of String) = Nothing
    ''' <summary>
    ''' 初始化音乐播放列表。
    ''' </summary>
    ''' <param name="ForceReload">强制全部重新载入列表。</param>
    ''' <param name="PreventFirst">在重载列表时避免让某项成为第一项。</param>
    Private Sub MusicListInit(ForceReload As Boolean, Optional PreventFirst As String = Nothing)
        If ForceReload Then MusicAllList = Nothing
        Try
            '初始化全部可用音乐列表
            If MusicAllList Is Nothing Then
                MusicAllList = New List(Of String)
                Directory.CreateDirectory(Path & "PCL\Musics\")
                For Each File In EnumerateFiles(Path & "PCL\Musics\")
                    '文件夹可能会被加入 .ini 文件夹配置文件、一些乱七八糟的 .jpg 文件啥的
                    Dim Ext As String = File.Extension.ToLower
                    If {".ini", ".jpg", ".txt", ".cfg", ".lrc", ".db", ".png"}.Contains(Ext) Then Continue For
                    MusicAllList.Add(File.FullName)
                Next
            End If
            '打乱顺序播放
            MusicWaitingList = If(Setup.Get("UiMusicRandom"), Shuffle(New List(Of String)(MusicAllList)), New List(Of String)(MusicAllList))
            If PreventFirst IsNot Nothing AndAlso MusicWaitingList.FirstOrDefault = PreventFirst Then
                '若需要避免成为第一项的为第一项，则将它放在最后
                MusicWaitingList.RemoveAt(0)
                MusicWaitingList.Add(PreventFirst)
            End If
        Catch ex As Exception
            Log(ex, "初始化音乐列表失败", LogLevel.Feedback)
        End Try
    End Sub
    ''' <summary>
    ''' 获取下一首播放的音乐路径并将其从列表中移除。
    ''' 如果没有，可能会返回 Nothing。
    ''' </summary>
    Private Function DequeueNextMusicAddress() As String
        '初始化，确保存在音乐
        If MusicAllList Is Nothing OrElse Not MusicAllList.Any() OrElse Not MusicWaitingList.Any() Then MusicListInit(False)
        '出列下一个音乐，如果出列结束则生成新列表
        If MusicWaitingList.Any() Then
            DequeueNextMusicAddress = MusicWaitingList(0)
            MusicWaitingList.RemoveAt(0)
        Else
            DequeueNextMusicAddress = Nothing
        End If
        If Not MusicWaitingList.Any() Then MusicListInit(False, DequeueNextMusicAddress)
    End Function

#End Region

#Region "UI 控制"

    ''' <summary>
    ''' 刷新背景音乐按钮 UI 与设置页 UI。
    ''' </summary>
    Private Sub MusicRefreshUI()
        RunInUi(
        Sub()
            Try

                If Not MusicAllList.Any() Then
                    '无背景音乐
                    FrmMain.BtnExtraMusic.Show = False
                Else
                    '有背景音乐
                    FrmMain.BtnExtraMusic.Show = True
                    Dim ToolTipText As String
                    If MusicState = MusicStates.Pause Then
                        FrmMain.BtnExtraMusic.Logo = Logo.IconPlay
                        FrmMain.BtnExtraMusic.LogoScale = 0.8
                        ToolTipText = "已暂停：" & GetFileNameWithoutExtentionFromPath(MusicCurrent)
                        If MusicAllList.Count > 1 Then
                            ToolTipText += vbCrLf & "左键恢复播放，右键播放下一曲。"
                        Else
                            ToolTipText += vbCrLf & "左键恢复播放，右键重新从头播放。"
                        End If
                    Else
                        FrmMain.BtnExtraMusic.Logo = Logo.IconMusic
                        FrmMain.BtnExtraMusic.LogoScale = 1
                        ToolTipText = "正在播放：" & GetFileNameWithoutExtentionFromPath(MusicCurrent)
                        If MusicAllList.Count > 1 Then
                            ToolTipText += vbCrLf & "左键暂停，右键播放下一曲。"
                        Else
                            ToolTipText += vbCrLf & "左键暂停，右键重新从头播放。"
                        End If
                    End If
                    FrmMain.BtnExtraMusic.ToolTip = ToolTipText
                    ToolTipService.SetVerticalOffset(FrmMain.BtnExtraMusic, If(ToolTipText.Contains(vbLf), 10, 16))
                End If
                If FrmSetupUI IsNot Nothing Then FrmSetupUI.MusicRefreshUI()

            Catch ex As Exception
                Log(ex, "刷新背景音乐 UI 失败", LogLevel.Feedback)
            End Try
        End Sub)
    End Sub

    ''' <summary>
    ''' 让音乐在暂停、播放间切换，并显示提示文本。
    ''' </summary>
    Public Sub MusicControlPause()
        If MusicNAudio Is Nothing Then
            Hint("音乐播放尚未开始！", HintType.Critical)
        Else
            Select Case MusicState
                Case MusicStates.Pause
                    MusicResume()
                Case MusicStates.Play
                    MusicPause()
                Case Else
                    Log("[Music] 音乐目前为停止状态，已强制尝试开始播放", LogLevel.Debug)
                    MusicRefreshPlay(False)
            End Select
        End If
    End Sub

    ''' <summary>
    ''' 播放下一曲，并显示提示文本。
    ''' </summary>
    Public Sub MusicControlNext()
        If MusicAllList.Count = 1 Then
            MusicStartPlay(MusicCurrent)
            Hint("重新播放：" & GetFileNameFromPath(MusicCurrent), HintType.Finish)
        Else
            Dim Address As String = DequeueNextMusicAddress()
            If Address Is Nothing Then
                Hint("没有可以播放的音乐！", HintType.Critical)
            Else
                MusicStartPlay(Address)
                Hint("正在播放：" & GetFileNameFromPath(Address), HintType.Finish)
            End If
        End If
        MusicRefreshUI()
    End Sub

#End Region

#Region "主状态控制"

    ''' <summary>
    ''' 获取当前的音乐播放状态。
    ''' </summary>
    Public ReadOnly Property MusicState As MusicStates
        Get
            If MusicNAudio Is Nothing Then Return MusicStates.Stop
            Select Case MusicNAudio.PlaybackState
                Case 0 'NAudio.Wave.PlaybackState.Stopped
                    Return MusicStates.Stop
                Case 2 'NAudio.Wave.PlaybackState.Paused
                    Return MusicStates.Pause
                Case Else
                    Return MusicStates.Play
            End Select
        End Get
    End Property
    Public Enum MusicStates
        [Stop]
        Play
        Pause
    End Enum

    '重载与开始

    ''' <summary>
    ''' 重载播放列表并尝试开始播放背景音乐。
    ''' </summary>
    ''' <param name="ShowHint">是否显示刷新提示。</param>
    Public Sub MusicRefreshPlay(ShowHint As Boolean, Optional IsFirstLoad As Boolean = False)
        Try

            MusicListInit(True)
            If Not MusicAllList.Any() Then
                If MusicNAudio Is Nothing Then
                    If ShowHint Then Hint("未检测到可用的背景音乐！", HintType.Critical)
                Else
                    MusicNAudio = Nothing
                    If ShowHint Then Hint("背景音乐已清除！", HintType.Finish)
                End If
            Else
                Dim Address As String = DequeueNextMusicAddress()
                If Address Is Nothing Then
                    If ShowHint Then Hint("没有可以播放的音乐！", HintType.Critical)
                Else
                    Try
                        MusicStartPlay(Address, IsFirstLoad)
                        If ShowHint Then Hint("背景音乐已刷新：" & GetFileNameFromPath(Address), HintType.Finish, False)
                    Catch
                    End Try
                End If
            End If
            MusicRefreshUI()

        Catch ex As Exception
            Log(ex, "刷新背景音乐播放失败", LogLevel.Feedback)
        End Try
    End Sub
    ''' <summary>
    ''' 开始播放音乐。
    ''' </summary>
    Private Sub MusicStartPlay(Address As String, Optional IsFirstLoad As Boolean = False)
        If Address Is Nothing Then Return
        If _smtc Is Nothing Then EnableSMTCSupport()
        Log("[Music] 播放开始：" & Address)
        MusicCurrent = Address
        RunInNewThread(Sub() MusicLoop(IsFirstLoad), "Music", ThreadPriority.BelowNormal)
    End Sub

    '播放与暂停

    ''' <summary>
    ''' 暂停音乐播放，返回是否成功切换了状态。
    ''' </summary>
    Public Function MusicPause() As Boolean
        If MusicState = MusicStates.Play Then
            RunInThread(
            Sub()
                Log("[Music] 已暂停播放")
                MusicNAudio?.Pause()
                SetSMTCStatus()
                MusicRefreshUI()
            End Sub)
            Return True
        Else
            Log($"[Music] 无需暂停播放，当前状态为 {MusicState}")
            Return False
        End If
    End Function
    ''' <summary>
    ''' 继续音乐播放，返回是否成功切换了状态。
    ''' </summary>
    Public Function MusicResume() As Boolean
        If MusicState = MusicStates.Play OrElse Not MusicAllList.Any() Then
            Log($"[Music] 无需继续播放，当前状态为 {MusicState}")
            Return False
        Else
            RunInThread(
            Sub()
                Log("[Music] 已恢复播放")
                MusicNAudio?.Play()
                SetSMTCStatus()
                MusicRefreshUI()
            End Sub)
            Return True
        End If
    End Function

#End Region

#Region "SMTC 控件"
    Private ReadOnly _player = New Playback.MediaPlayer
    Private _smtc As SystemMediaTransportControls = Nothing
    Public MediaTitle As String = Nothing
    Public MediaArtist As String() = Nothing
    ''' <summary>
    ''' 启用 SMTC 支持
    ''' </summary>
    Public Sub EnableSMTCSupport()
        Log("[SMTC] 初始化 SMTC 支持")
        '初始化
        _player.CommandManager.IsEnabled = False
        _smtc = _player.SystemMediaTransportControls
        _smtc.IsEnabled = True

        '设置可交互性
        _smtc.IsPlayEnabled = True
        _smtc.IsPauseEnabled = True
        _smtc.IsNextEnabled = True
        _smtc.IsPreviousEnabled = False '暂时没有上一首
    End Sub
    ''' <summary>
    ''' 获取媒体文件信息
    ''' </summary>
    ''' <param name="file">文件路径</param>
    Public Async Sub GetMediaFileInfo(path As String)
        'Dim openpicker = New FileOpenPicker()
        'Dim file As StorageFile = Await openpicker.PickSingleFileAsync()
        'Dim filestream = Await file.OpenStreamForReadAsync()

        'Dim tag As TagLib.File = TagLib.File.Create(New StreamFileAbstraction(path, filestream, filestream))

        'MediaTitle = tag.Tag.Title
        'MediaArtist = tag.Tag.AlbumArtists
    End Sub
    ''' <summary>
    ''' 更新 SMTC 信息
    ''' </summary>
    Public Sub UpdateSMTCInfo()
        Log($"[SMTC] 更新 SMTC 媒体信息，标题: {MediaTitle}，艺术家: {MediaArtist}")
        Dim Updater = _smtc.DisplayUpdater

        Updater.AppMediaId = "Plain Craft Launcher 2 CE" '媒体来源信息
        Updater.Type = MediaPlaybackType.Music '指定媒体类型
        If Not MediaTitle = Nothing Then Updater.MusicProperties.Title = MediaTitle '标题
        If Not MediaArtist(0) = Nothing Then Updater.MusicProperties.Artist = MediaArtist(0) '标题

        '生效设置
        Updater.Update()
    End Sub
    ''' <summary>
    ''' 设置 SMTC 媒体播放状态
    ''' </summary>
    Public Sub SetSMTCStatus()
        If MusicState = MusicStates.Play Then
            Log("[SMTC] 更新 SMTC 播放状态为：Playing")
            _smtc.PlaybackStatus = MediaPlaybackStatus.Playing
        ElseIf MusicState = MusicStates.Pause Then
            Log("[SMTC] 更新 SMTC 播放状态为：Paused")
            _smtc.PlaybackStatus = MediaPlaybackStatus.Paused
        Else
            Log("[SMTC] 更新 SMTC 播放状态为：Stopped")
            _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped
        End If
    End Sub

    ''' <summary>
    ''' 响应 SMTC 交互
    ''' </summary>
    Public Sub _smtc_ButtonPressed(sender As SystemMediaTransportControls, args As SystemMediaTransportControlsButtonPressedEventArgs)
        Select Case args.Button
            Case SystemMediaTransportControlsButton.Play
                Log("[SMTC] 收到 SMTC 控件事件，切换播放状态为：Playing")
                MusicResume()
                Exit Select
            Case SystemMediaTransportControlsButton.Pause
                Log("[SMTC] 收到 SMTC 控件事件，切换播放状态为：Paused")
                MusicPause()
                Exit Select
            Case SystemMediaTransportControlsButton.Next
                Log("[SMTC] 收到 SMTC 控件事件，切换到下一曲")
                MusicControlNext()
                Exit Select
        End Select
    End Sub

    ''' <summary>
    ''' 更新 SMTC 时间线属性
    ''' </summary>
    ''' <param name="CurrentTime">当前播放进度</param>
    ''' <param name="TotalTime">曲目全长</param>
    Public Sub UpdateSMTCTimeline(CurrentTime As Integer, TotalTime As Integer)
        Dim Properties = New SystemMediaTransportControlsTimelineProperties

        Properties.StartTime = TimeSpan.FromSeconds(0)
        Properties.MinSeekTime = TimeSpan.FromSeconds(0)
        Properties.Position = TimeSpan.FromSeconds(CurrentTime)
        Properties.MaxSeekTime = TimeSpan.FromSeconds(TotalTime)
        Properties.EndTime = TimeSpan.FromSeconds(TotalTime)

        _smtc.UpdateTimelineProperties(Properties)
    End Sub
    ''' <summary>
    ''' 以 1000 ms 为刷新间隔的 SMTC 时间线更新
    ''' </summary>
    Public Sub SMTCTimelineUpdater(CurrentWave As NAudio.Wave.WaveOut, Reader As NAudio.Wave.WaveStream)
        While CurrentWave.Equals(MusicNAudio) AndAlso CurrentWave.PlaybackState = NAudio.Wave.PlaybackState.Playing
            RunInNewThread(Sub() UpdateSMTCTimeline(Reader.CurrentTime.TotalMilliseconds, Reader.TotalTime.TotalMilliseconds))
            Thread.Sleep(1000)
        End While
    End Sub
#End Region

    ''' <summary>
    ''' 当前正在播放的 NAudio.Wave.WaveOut。
    ''' </summary>
    Public MusicNAudio = Nothing
    ''' <summary>
    ''' 当前播放的音乐地址。
    ''' </summary>
    Private MusicCurrent As String = ""

    ''' <summary>
    ''' 在 MusicUuid 不变的前提下，持续播放某地址的音乐，且在播放结束后随机播放下一曲。
    ''' </summary>
    Private Sub MusicLoop(Optional IsFirstLoad As Boolean = False)
        Dim CurrentWave As NAudio.Wave.WaveOut = Nothing
        Dim Reader As NAudio.Wave.WaveStream = Nothing
        GetMediaFileInfo(MusicCurrent)
        Try
            '开始播放
            CurrentWave = New NAudio.Wave.WaveOut()
            MusicNAudio = CurrentWave
            Reader = New NAudio.Wave.AudioFileReader(MusicCurrent)
            CurrentWave.Init(Reader)
            CurrentWave.Play()
            SetSMTCStatus()
            '第一次打开的暂停
            If IsFirstLoad AndAlso Not Setup.Get("UiMusicAuto") Then
                CurrentWave.Pause()
                EnableSMTCSupport()
                UpdateSMTCInfo()
            End If
            MusicRefreshUI()
            '停止条件：播放完毕或变化
            Dim PreviousVolume = 0
            SMTCTimelineUpdater(CurrentWave, Reader)
            While CurrentWave.Equals(MusicNAudio) AndAlso Not CurrentWave.PlaybackState = NAudio.Wave.PlaybackState.Stopped
                If Setup.Get("UiMusicVolume") <> PreviousVolume Then
                    '更新音量
                    PreviousVolume = Setup.Get("UiMusicVolume")
                    CurrentWave.Volume = PreviousVolume / 1000
                End If
                '更新进度条
                Dim Percent = Reader.CurrentTime.TotalMilliseconds / Reader.TotalTime.TotalMilliseconds
                RunInUi(Sub() FrmMain.BtnExtraMusic.Progress = Percent)
                Thread.Sleep(100)
            End While
            '当前音乐已播放结束，继续下一曲
            If CurrentWave.PlaybackState = NAudio.Wave.PlaybackState.Stopped AndAlso MusicAllList.Any Then MusicStartPlay(DequeueNextMusicAddress)
        Catch ex As Exception
            Log(ex, "播放音乐出现内部错误（" & MusicCurrent & "）", LogLevel.Developer)
            If TypeOf ex Is NAudio.MmException AndAlso (ex.Message.Contains("NoDriver") OrElse ex.Message.Contains("BadDeviceId")) Then
                Hint("由于音频设备变更，音乐播放功能在重启 PCL 后才能恢复！", HintType.Critical)
                Thread.Sleep(1000000000)
            End If
            If ex.Message.Contains("Got a frame at sample rate") OrElse ex.Message.Contains("does not support changes to") Then
                Hint("播放音乐失败（" & GetFileNameFromPath(MusicCurrent) & "）：PCL 不支持播放音频属性在中途发生变化的音乐", HintType.Critical)
            ElseIf Not (MusicCurrent.EndsWithF(".wav", True) OrElse MusicCurrent.EndsWithF(".mp3", True) OrElse MusicCurrent.EndsWithF(".flac", True)) OrElse
                ex.Message.Contains("0xC00D36C4") Then '#5096：不支持给定的 URL 的字节流类型。 (异常来自 HRESULT:0xC00D36C4)
                Hint("播放音乐失败（" & GetFileNameFromPath(MusicCurrent) & "）：PCL 可能不支持此音乐格式，请将格式转换为 .wav、.mp3 或 .flac 后再试", HintType.Critical)
            Else
                Log(ex, "播放音乐失败（" & GetFileNameFromPath(MusicCurrent) & "）", LogLevel.Hint)
            End If
            '将播放错误的音乐从列表中移除
            MusicAllList.Remove(MusicCurrent)
            MusicWaitingList.Remove(MusicCurrent)
            MusicRefreshUI()
            '等待 2 秒后继续播放
            Thread.Sleep(2000)
            If TypeOf ex Is FileNotFoundException Then
                MusicRefreshPlay(True, IsFirstLoad)
            Else
                MusicStartPlay(DequeueNextMusicAddress(), IsFirstLoad)
            End If
        Finally
            If CurrentWave IsNot Nothing Then CurrentWave.Dispose()
            If Reader IsNot Nothing Then Reader.Dispose()
            MusicRefreshUI()
        End Try
    End Sub

End Module
