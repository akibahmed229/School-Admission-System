import { ApiError } from '../api';

/** Turns any failure into a sentence a parent or school admin can understand. */
export function describeError(e: unknown): string {
  if (!(e instanceof ApiError)) return 'কিছু একটা সমস্যা হয়েছে। অনুগ্রহ করে আবার চেষ্টা করুন।';

  switch (e.code) {
    case 'NETWORK':
      return 'সার্ভারের সাথে যোগাযোগ করা যাচ্ছে না। ইন্টারনেট বা সার্ভার চালু আছে কিনা দেখে একটু পরে আবার চেষ্টা করুন।';
    case 'NO_SEATS_AVAILABLE':
      return 'দুঃখিত, এই class-এ এখন কোনো খালি seat নেই। অন্য shift দেখুন অথবা পরে আবার চেষ্টা করুন।';
    case 'ALREADY_APPLIED':
      return 'আপনি এই class-এ ইতোমধ্যে আবেদন করেছেন। "আমার আবেদন" অংশে দেখুন।';
    case 'ALREADY_ADMITTED':
      return 'এই class-এ আপনার ভর্তি ইতোমধ্যে নিশ্চিত হয়েছে।';
    case 'HOLD_EXPIRED':
      return 'আবেদনের মেয়াদ শেষ হয়ে গেছে, seat ছেড়ে দেওয়া হয়েছে। আবার আবেদন করুন।';
    case 'HOLD_CANCELLED':
      return 'এই আবেদন আগেই বাতিল করা হয়েছে।';
    case 'CANNOT_CANCEL_CONFIRMED':
      return 'ভর্তি নিশ্চিত হয়ে গেছে, তাই এখন আর বাতিল করা যাবে না।';
    case 'FORBIDDEN':
      return 'এই আবেদনটি আপনার জন্ম নিবন্ধন নম্বরের নয়।';
    case 'CLASS_NOT_FOUND':
    case 'HOLD_NOT_FOUND':
    case 'NOT_FOUND':
      return 'যা খুঁজছেন তা পাওয়া যায়নি। পাতাটি refresh করে দেখুন।';
    case 'MISSING_STUDENT_ID':
    case 'INVALID_STUDENT_ID':
      return 'প্রথমে উপরের ঘরে শিক্ষার্থীর ১৭ অঙ্কের জন্ম নিবন্ধন নম্বর দিন।';
    case 'VALIDATION_ERROR':
    case 'BAD_REQUEST':
      return `ইনপুট ঠিক নেই: ${e.message}`;
    default:
      return e.status >= 500
        ? 'সার্ভারে একটি সমস্যা হয়েছে। কিছুক্ষণ পরে আবার চেষ্টা করুন।'
        : 'অনুরোধটি সম্পন্ন করা যায়নি। অনুগ্রহ করে আবার চেষ্টা করুন।';
  }
}
