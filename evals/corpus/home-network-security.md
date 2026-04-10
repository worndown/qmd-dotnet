# Home Network Security

## Router Configuration

Your router is the gateway between your home network and the internet. Start by changing the default administrator credentials — factory defaults (admin/admin, admin/password) are well-known and trivially exploitable. Use a strong, unique password for the admin interface.

### Firmware Updates

Router manufacturers regularly patch vulnerabilities. Enable automatic firmware updates if available, or check manually every month. Running outdated firmware is one of the most common attack vectors for home networks. If your router is end-of-life and no longer receives updates, replace it.

### Disable WPS and UPnP

Wi-Fi Protected Setup (WPS) has a well-known brute-force vulnerability in its PIN mode that can be exploited in hours. Disable it entirely. Universal Plug and Play (UPnP) automatically opens ports on your router when devices request it — convenient, but it allows any malware on your network to punch holes in your firewall. Disable UPnP unless you have a specific need and understand the risk.

## Wi-Fi Security

### WPA3 and WPA2

Use WPA3 (Wi-Fi Protected Access 3) if all your devices support it. WPA3 provides Simultaneous Authentication of Equals (SAE), which replaces the PSK 4-way handshake and is resistant to offline dictionary attacks. If you have older devices, use WPA2/WPA3 transitional mode. Never use WEP or WPA (without the "2" or "3") — both are broken.

### SSID and Password

Choose a password of at least 16 characters. Hiding your SSID does not meaningfully improve security — the network name is still broadcast in probe responses and association frames. It only inconveniences legitimate users.

## Network Segmentation

### Guest Network

Most modern routers support a guest network — a separate Wi-Fi SSID with its own password that is isolated from your main network. Put IoT devices (smart speakers, cameras, thermostats) on the guest network. This limits the blast radius if an IoT device is compromised: the attacker cannot pivot to your computers, phones, or NAS.

### VLAN Configuration

For more advanced segmentation, use VLANs (Virtual Local Area Networks). A typical home setup might use three VLANs: one for trusted devices (computers, phones), one for IoT devices, and one for guests. This requires a managed switch and a router or firewall capable of inter-VLAN routing with access control lists.

## DNS-Level Filtering

Use a DNS resolver that blocks known malicious domains. Options include:

- **Pi-hole**: Self-hosted DNS sinkhole that blocks ads and malware domains at the network level. Runs on a Raspberry Pi or any Linux machine.
- **NextDNS / Cloudflare Gateway**: Cloud-based DNS filtering with block lists and logging.
- **Quad9 (9.9.9.9)**: Free recursive DNS resolver that blocks known-malicious domains using threat intelligence feeds.

Configure your router's DHCP settings to push your chosen DNS resolver to all clients, or set it as the router's own upstream DNS.

## Firewall and Port Management

Your router's firewall should block all unsolicited inbound connections by default (this is the default for most consumer routers). Never forward ports unless you understand exactly what service you're exposing and why. If you need remote access, use a VPN (WireGuard is fast and simple to configure) rather than opening SSH or RDP directly to the internet.

## Monitoring and Alerts

Check your router's connected-device list periodically. Unknown MAC addresses may indicate unauthorized access. Some routers and firewalls (pfSense, OPNsense, UniFi) support alerting on new device connections or unusual traffic patterns.

## Password Management

Use a password manager (Bitwarden, KeePass, 1Password) for all accounts. Every account should have a unique, randomly generated password of at least 16 characters. Enable two-factor authentication (TOTP or hardware keys like YubiKey) wherever supported, especially for email, banking, and cloud storage.

## Backup Strategy

Follow the 3-2-1 rule: keep three copies of important data, on two different types of media, with one copy offsite. A NAS with RAID provides local redundancy; a cloud backup (Backblaze B2, Wasabi, or encrypted rclone to any S3-compatible provider) provides geographic redundancy. Test restores periodically — a backup you haven't tested is a backup you don't have.
