# Linux Server Administration

## Initial Server Setup

After provisioning a fresh Linux server (Debian, Ubuntu, or RHEL-based), the first steps are securing access and hardening the system.

### Create a Non-Root User

Never operate as root for daily tasks. Create an unprivileged user and add it to the sudo group:

```
adduser deploy
usermod -aG sudo deploy
```

### SSH Key Authentication

Disable password-based SSH login entirely. Copy your public key to `~/.ssh/authorized_keys` on the server and set `PasswordAuthentication no` in `/etc/ssh/sshd_config`. Also set `PermitRootLogin no` to prevent direct root login over SSH. Restart sshd after changes.

Use Ed25519 keys (`ssh-keygen -t ed25519`) — they are shorter, faster, and more secure than RSA keys. If you must use RSA, use at least 4096 bits.

### Firewall Configuration

Use `ufw` (Uncomplicated Firewall) on Ubuntu/Debian or `firewalld` on RHEL-based systems. The principle is default-deny: block everything, then open only the ports you need.

```
ufw default deny incoming
ufw default allow outgoing
ufw allow 22/tcp       # SSH
ufw allow 80/tcp       # HTTP
ufw allow 443/tcp      # HTTPS
ufw enable
```

For servers behind a load balancer or VPN, restrict SSH access to specific IP ranges with `ufw allow from 10.0.0.0/24 to any port 22`.

## Package Management

### Debian/Ubuntu (apt)

Keep the system updated: `apt update && apt upgrade -y`. Enable unattended security updates with `apt install unattended-upgrades` and configure `/etc/apt/apt.conf.d/50unattended-upgrades`. This automatically applies critical security patches without manual intervention.

### RHEL/CentOS/Rocky (dnf/yum)

Use `dnf update` to apply all updates. Enable automatic security updates with `dnf-automatic`. Configure it in `/etc/dnf/automatic.conf` to apply only security updates and send email notifications.

## Process Management with systemd

Most modern Linux distributions use systemd for service management. Key commands:

- `systemctl start nginx` — start a service
- `systemctl enable nginx` — start on boot
- `systemctl status nginx` — check if running, recent logs
- `journalctl -u nginx -f` — follow logs for a specific service
- `journalctl --since "1 hour ago"` — view recent system logs

### Writing a Service File

To run your own application as a service, create a unit file in `/etc/systemd/system/myapp.service`:

```
[Unit]
Description=My Application
After=network.target

[Service]
User=deploy
WorkingDirectory=/opt/myapp
ExecStart=/opt/myapp/run.sh
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Run `systemctl daemon-reload` after creating or modifying unit files.

## File Permissions and Ownership

Linux permissions follow the user/group/other model. Each file has read (r=4), write (w=2), and execute (x=1) bits for owner, group, and others.

Common patterns:
- `chmod 755 script.sh` — owner can read/write/execute; group and others can read/execute
- `chmod 600 private-key.pem` — only the owner can read and write
- `chown -R deploy:deploy /opt/myapp` — set owner and group recursively

The sticky bit (`chmod +t /tmp`) prevents users from deleting files they don't own in shared directories. The setuid bit (`chmod u+s`) runs a program with the file owner's privileges — powerful but dangerous; audit any setuid binaries on your system.

## Monitoring and Diagnostics

- **htop**: Interactive process viewer. Shows CPU, memory, and swap usage per process. Press F5 for tree view to see parent-child relationships.
- **df -h**: Disk space usage by filesystem. Watch for filesystems approaching 90% — many systems misbehave when disk is full.
- **du -sh /var/log/**: Identify which directories consume the most space.
- **iostat**: Disk I/O statistics. High iowait percentages indicate the disk is a bottleneck.
- **netstat -tulnp** or **ss -tulnp**: List open ports and the processes using them. Verify only expected services are listening.
- **dmesg**: Kernel messages. Check here for hardware errors, OOM (Out of Memory) kills, and filesystem errors.

## Log Management

Logs accumulate rapidly on busy servers. Configure logrotate to rotate, compress, and prune logs automatically. The default configuration (`/etc/logrotate.conf`) rotates weekly and keeps 4 weeks. For high-volume applications, set daily rotation with `maxsize 100M` to prevent runaway log files from filling the disk.

Centralized logging with rsyslog, Loki, or the ELK stack (Elasticsearch, Logstash, Kibana) is essential for multi-server environments. Ship logs off-server so that if a machine is compromised or fails, the logs survive.

## Backup and Recovery

### Filesystem Snapshots

Use LVM snapshots or ZFS/btrfs snapshots for instant, consistent backups. A snapshot captures the state of a filesystem at a point in time with minimal performance overhead. Schedule daily snapshots and prune older ones automatically.

### Off-Site Backup

Use rsync over SSH, restic, or Borg to push encrypted backups to a remote server or object storage (S3, Backblaze B2). Test restores regularly — an untested backup is not a backup.

### Disaster Recovery Plan

Document the full rebuild procedure for each server: what packages are installed, what configuration files are customized, where secrets are stored. Configuration management tools like Ansible, Salt, or Puppet codify this into repeatable playbooks.
