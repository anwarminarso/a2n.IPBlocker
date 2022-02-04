# a2n.IPBlocker

a2n.IPBlocker adalah aplikasi kecil yang secara automatis menambahkan daftar IP white list dan IP banned/black list. 
Aplikasi ini sangat tergantung pada file /etc/hosts.allow dan /etc/hosts.deny.
Trigger automasi terjadi ketika ada user malakukan login ssh/sshd ke server linux, denngan kondisi:
Jika IP belum terdaftar
- Jika pertama kali gagal login, maka secara automatis dimasukan ke daftar black list
- Jika pertama kali berhasil login, maka secara automatis dimasukan ke daftar white list
Dan jika IP sudah terdaftar pada white list, tidak akan masuk ke black list atau white list lagi 


### INSTALL .NET 5 atau 6
Ikuti langkah pada link berikut ini [Install .NET on Linux](https://docs.microsoft.com/en-us/dotnet/core/install/linux)
setelah berhasil install dotnet dapat melakukan pengecheckan dengan perintah:
```
dotnet --version
```
dan untuk mengetahuhi letak file dotnetnya dapat dilakukan dengan perintah:
```
which dotnet
```

### Extract file
Extract file folder IPBloker ke local server

### IPBlocker Service
Agar aplikasi berjalan pada saat startup
```bash
sudo nano /etc/systemd/system/ipblocker.service
```

pada isi file perlu diperhatikan paramter Working Directory dan ExecStart
pada contoh dibawah WorkingDirectory ``/home/IPBlocker`` adalah folder dimana terdapat file ``a2n.IPBlocker.dll`` dan folder ``/usr/local/bin/dotnet`` adalah path yg didapat dengan cara ``which dotnet``
```
[Unit]
Description=a2n IPBlocker

[Service]
WorkingDirectory=/home/IPBlocker
ExecStart=/usr/local/bin/dotnet /home/IPBlocker/a2n.IPBlocker.dll
Restart=always
# Restart service after 10 seconds if the dotnet service crashes:
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=dotnet-ipblocker
User=root
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

[Install]
WantedBy=multi-user.target
```


### Enable Service
```bash
sudo systemctl enable ipblocker.service
```

### Start Service
```bash
sudo systemctl start ipblocker
```

### Check Service Status
```bash
sudo systemctl status ipblocker
```
atau

```bash
journalctl -fu ipblocker
```
