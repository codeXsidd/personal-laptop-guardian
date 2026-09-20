import 'package:flutter/material.dart';

enum EventCategory {
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
      agentStarted => 'Agent Started',
      systemStartup => 'System Startup',
      systemShutdown => 'System Shutdown',
      sessionLogin => 'Login',
      sessionLogout => 'Logout',
      sessionLock => 'Screen Lock',
      sessionUnlock => 'Screen Unlock',
      loginFailed => 'Login Failed',
      processStart => 'App Started',
      processStop => 'App Stopped',
      usbConnected => 'USB Connected',
      usbDisconnected => 'USB Disconnected',
      networkConnected => 'Network Connected',
      networkDisconnected => 'Network Disconnected',
      networkChanged => 'Network Changed',
      eventlogEntry => 'Event Log',
      fileAccess => 'File Access',
      systemMetrics => 'System Metrics',
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

  static EventCategory category(String type) {
    if (sessionTypes.contains(type)) return EventCategory.session;
    if (processTypes.contains(type)) return EventCategory.process;
    if (usbTypes.contains(type)) return EventCategory.usb;
    if (networkTypes.contains(type)) return EventCategory.network;
    if (type == fileAccess) return EventCategory.file;
    if (type == eventlogEntry) return EventCategory.eventLog;
    return EventCategory.system;
  }
}
