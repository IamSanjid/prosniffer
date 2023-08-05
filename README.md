# PROSniffer
This is a custom packet sniffer(inspects all the communication between the server and the client) for Pokemon Revolution Online(https://pokemonrevolution.net/).

# Dependencies
[SharpCap](https://github.com/dotpcap/sharppcap)

# How to use?
Follow [SharpCap's](https://github.com/dotpcap/sharppcap) read me. <br>
Easy setup: 
* Windows: Install [NpCap's](https://npcap.com/#download) dependencies.
* Linux: Install [libcap](https://www.tcpdump.org/manpages/pcap.3pcap.html) dependencies.
* MacOs: Check out [SharpCap's](https://github.com/dotpcap/sharppcap) guide, otherwise just install [WireShark](https://www.wireshark.org/docs/wsug_html_chunked/ChBuildInstallOSXInstall.html).

Make sure you start to sniff the packets before logging in, otherwise won't be able to retain the proper RC4 state. <br>
Also make sure that no other application is connected through port 800 or 801 to some remote ipaddress other than PRO(Just close some other not really important applications?). <br>
You can continue writing on the standard input(console) while packets are being written on the standard output(console) to provide your next command. <br>

Commands:
```
interfaces|i
   Desc: Shows all the eathernet/wireless interfaces on your machine.
sniff i=[interface index] <p|port=[ushort]; default is 800(Silver Server), provide 801 for Gold Server> <custom filter>
   Desc: Starts sniffing, if no argument is provided uses last provided arguments. 
         If you want to provide a custom filter like wireshark advance filters to detect PRO communication you can do that.
         Just provide it this way: sniff i=[index] cf="your filter".
filter|f
   Desc: You can provide custom Regex pattern to filter out received packets.
pause|p|resume|r
   Desc: Pauses/Resumes from printing/logging packets.
clear|cls
   Desc: Clears the console screen, doesn't clear the internal packet log(which is used if you want to dump packets when quiting normally).
dump <file name> 
   Desc: Dumps all the packets inside the "Dumps" folder.
exit|q
   Desc: Exits normally also dumps all the packets to a file if dump command was provided previously, check "Dumps" folder.
h|help
   Desc: Prints out this message.
```

Example:
```
Interfaces:
[0]: WAN Miniport (Network Monitor)
[1]: WAN Miniport (IPv6)
[2]: WAN Miniport (IP)
[3]: VMware Virtual Ethernet Adapter for VMnet8
[4]: VMware Virtual Ethernet Adapter for VMnet1
[5]: Realtek Gaming 2.5GbE Family Controller
[6]: Adapter for loopback traffic capture
```

A list like above should be printed, if it doesn't just put `i` and enter.<br>
As you can see for the above case it's obvious that index no 5 is the proper interface to care about.<br>
So for this specific case the user should put the follwing command if the user is going to login in to the `Gold` server.

```
sniff i=5 port=801
```

If the user is going to log in to the `Silver` server the command should be like this.

```
sniff i=5 port=800
```
