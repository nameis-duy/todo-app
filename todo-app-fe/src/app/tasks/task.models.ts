export type TaskStatus = 'Pending' | 'InProgress' | 'Completed';
export type TaskPriority = 'Low' | 'Normal' | 'High' | 'Urgent';
export type TaskSort = 'createdAt' | 'updatedAt' | 'title' | 'dueDate' | 'priority';

export interface TodoTask {
  id: string;
  title: string;
  description: string | null;
  status: TaskStatus;
  priority: TaskPriority;
  startDate: string | null;
  dueDate: string | null;
  completedAt: string | null;
  createdAt: string;
  updatedAt: string;
  version: string;
  purposeId: string | null;
  purposeTitle: string | null;
  ownerId: string | null;
  isOwner: boolean;
  canEdit: boolean;
}

export interface SaveTask {
  title: string;
  description: string | null;
  status: TaskStatus;
  priority: TaskPriority;
  startDate: string | null;
  dueDate: string | null;
  version?: string;
  purposeId: string | null;
}

export interface TaskQuery {
  search: string;
  status: TaskStatus | '';
  priority: TaskPriority | '';
  dueFrom: string;
  dueTo: string;
  sortBy: TaskSort;
  descending: boolean;
  page: number;
  pageSize: number;
  purposeId: string;
  uncategorized: boolean;
}

export interface TaskPage {
  items: TodoTask[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ApiProblem {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}