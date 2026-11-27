# StinkyBot
A simple music discord bot which purpose is to play music from Youtube or create and playing music lists.
Uses Discord.Net for Discord integration and FFmpeg for audio processing. Required native libraries (Opus, Sodium, and DAVE) are managed through NuGet packages.

Current Commands:

!summon                              summons the bot to the voice channel user is
!leave                               leaves the voice channel
!song + name of the song             searches the closest match from Youtube and plays the song
!skip                                skips the current song playing, also moves to the next song of the music list if used when bot plays music from music list
!addurlsong + Youtube URL            add song music list
!addsong                             add song music list
!removeurlsong + Youtube URL         remove song from music list
!removesong                          remove song from music list
!playmusiclist                       play music list
!stopmusiclist                       stop music list, used to skip all songs from that list
!musiclist                           show current songs in the music list
!commands                            show all commands the bot has
!testtone                            plays a test tone (for testing purposes)
!testaudio                           plays a mp4 (for testing purposes)
