import 'package:flutter/material.dart';

import '../../models/event_type.dart';
import 'event_list_screen.dart';

class EventLogScreen extends StatelessWidget {
  final String deviceId;

  const EventLogScreen({super.key, required this.deviceId});

  @override
  Widget build(BuildContext context) {
    return EventListScreen(
      deviceId: deviceId,
      filterEventType: EventTypes.eventlogEntry,
      title: 'Windows Event Log',
    );
  }
}
