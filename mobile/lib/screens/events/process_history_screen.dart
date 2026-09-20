import 'package:flutter/material.dart';

import '../../models/event_type.dart';
import 'event_list_screen.dart';

class ProcessHistoryScreen extends StatelessWidget {
  final String deviceId;

  const ProcessHistoryScreen({super.key, required this.deviceId});

  @override
  Widget build(BuildContext context) {
    return EventListScreen(
      deviceId: deviceId,
      filterEventTypes: EventTypes.processTypes,
      title: 'Application History',
    );
  }
}
