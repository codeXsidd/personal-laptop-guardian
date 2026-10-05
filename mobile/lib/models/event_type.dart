import 'package:flutter/material.dart';

enum EventCategory {
  power,
  session,
  process,
  usb,
  network,
  system,
  file,
  eventLog,
}

abstract final class EventTypes {
  static const agentStarted = 'agent_started';
  static const systemStartup = 'system_startup';
  static const systemShutdown = 'system_shutdown';
  static const sessionLogin = 'session_login';
  static const sessionLogout = 'session_logout';
  static const sessionLock = 'session_lock';
  static const sessionUnlock = 'session_unlock';
  static const loginFailed = 'login_failed';
  static const processStart = 'process_start';
  static const processStop = 'process_stop';
  static const usbConnected = 'usb_connected';
  static const usbDisconnected = 'usb_disconnected';
  static const networkConnected = 'network_connected';
  static const networkDisconnected = 'network_disconnected';
  static const networkChanged = 'network_changed';
  static const eventlogEntry = 'eventlog_entry';
  static const fileAccess = 'file_access';
  static const systemMetrics = 'system_metrics';
  static const systemSleep = 'system_sleep';
  static const systemWake = 'system_wake';

  static const sessionTypes = [
    sessionLogin,
    sessionLogout,
    sessionLock,
    sessionUnlock,
    loginFailed,
  ];

  static const processTypes = [processStart, processStop];

  static const usbTypes = [usbConnected, usbDisconnected];

  static const networkTypes = [
    networkConnected,
    networkDisconnected,
    networkChanged,
  ];

  static String displayName(String type) {
    return switch (type) {
      agentStarted => 'Laptop Guardian Started',
      systemStartup => 'Laptop Turned ON',
      systemShutdown => 'Laptop Shutting Down',
      sessionLogin => 'Windows User Logged In',
      sessionLogout => 'Windows User Logged Out',
      sessionLock => 'PC Locked',
      sessionUnlock => 'PC Unlocked',
      loginFailed => 'Login Attempt Failed',
      processStart => 'App Started',
      processStop => 'App Stopped',
      usbConnected => 'USB Device Connected',
      usbDisconnected => 'USB Device Disconnected',
      networkConnected => 'Network Connected',
      networkDisconnected => 'Network Disconnected',
      networkChanged => 'Network Changed',
      eventlogEntry => 'Event Log',
      fileAccess => 'File Accessed',
      systemMetrics => 'System Metrics',
      systemSleep => 'Laptop Sleeping',
      systemWake => 'Laptop Woke Up',
      _ => type.replaceAll('_', ' '),
    };
  }

  static IconData icon(String type) {
    return switch (type) {
      agentStarted => Icons.play_circle_outline,
      systemStartup => Icons.power_settings_new,
      systemShutdown => Icons.power_off,
      sessionLogin => Icons.login,
      sessionLogout => Icons.logout,
      sessionLock => Icons.lock,
      sessionUnlock => Icons.lock_open,
      loginFailed => Icons.error_outline,
      processStart => Icons.launch,
      processStop => Icons.stop_circle_outlined,
      usbConnected => Icons.usb,
      usbDisconnected => Icons.usb_off,
      networkConnected => Icons.wifi,
      networkDisconnected => Icons.wifi_off,
      networkChanged => Icons.swap_horiz,
      eventlogEntry => Icons.article_outlined,
      fileAccess => Icons.folder_open,
      systemMetrics => Icons.monitor_heart_outlined,
      systemSleep => Icons.nightlight_round,
      systemWake => Icons.wb_sunny,
      _ => Icons.event_note,
    };
  }

  static Color severityColor(String severity) {
    return switch (severity) {
      'critical' => Colors.red.shade700,
      'high' => Colors.orange.shade700,
      'medium' => Colors.amber.shade700,
      'low' => Colors.blue.shade600,
      _ => Colors.grey.shade600,
    };
  }

  static const powerTypes = [agentStarted, systemStartup, systemShutdown, systemSleep, systemWake];

  static EventCategory category(String type) {
    if (powerTypes.contains(type)) return EventCategory.power;
    if (sessionTypes.contains(type)) return EventCategory.session;
    if (processTypes.contains(type)) return EventCategory.process;
    if (usbTypes.contains(type)) return EventCategory.usb;
    if (networkTypes.contains(type)) return EventCategory.network;
    if (type == fileAccess) return EventCategory.file;
    if (type == eventlogEntry) return EventCategory.eventLog;
    return EventCategory.system;
  }

  static String categoryLabel(EventCategory cat) {
    return switch (cat) {
      EventCategory.power => 'POWER',
      EventCategory.session => 'USER',
      EventCategory.process => 'APP',
      EventCategory.usb => 'USB',
      EventCategory.network => 'NETWORK',
      EventCategory.system => 'SYSTEM',
      EventCategory.file => 'FILE',
      EventCategory.eventLog => 'LOG',
    };
  }
}
