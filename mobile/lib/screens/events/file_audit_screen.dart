import 'package:flutter/material.dart';

import '../../models/event_type.dart';
import 'event_list_screen.dart';

class FileAuditScreen extends StatelessWidget {
  final String deviceId;

  const FileAuditScreen({super.key, required this.deviceId});

  @override
  Widget build(BuildContext context) {
    return EventListScreen(
      deviceId: deviceId,
      filterEventType: EventTypes.fileAccess,
      title: 'File Access Audit',
    );
  }
}
