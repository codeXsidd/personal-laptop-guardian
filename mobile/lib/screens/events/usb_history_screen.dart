import 'package:flutter/material.dart';

import '../../models/event_type.dart';
import 'event_list_screen.dart';

class UsbHistoryScreen extends StatelessWidget {
  final String deviceId;

  const UsbHistoryScreen({super.key, required this.deviceId});

  @override
  Widget build(BuildContext context) {
    return EventListScreen(
      deviceId: deviceId,
      filterEventTypes: EventTypes.usbTypes,
      title: 'USB Device History',
    );
  }
}
